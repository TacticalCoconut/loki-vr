using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 4 (w4fx; w3combat's leftover): a monster killed by a hit plays its death with that hit, not when ZC_NOTIFY_VANISH (type 1,
    /// CLR_DEAD) arrives. rAthena sends the vanish when the damage is applied (status_damage → unit_remove_map, clif_clearunit_area), while
    /// the client shows the hit later, on its own schedule (Game/HitSchedule.cs: the attacker's damage frame / attackMT + 200·i). roBrowserLegacy
    /// (Engine/MapEngine/Entity.js:366-392, 2705-2708; a reimplementation, the only reference) keeps the unit until the last damaging hit on it
    /// has shown, plus C_DEATH_SYNC_OFFSET = 200 ms (Entity.js:97), for any non-player unit; the id is freed at once (Entity.js:380-386).
    /// Players are unchanged: a dead player stays on the ground (EntityManager's vanish arm).
    /// </summary>
    public static class DeathSync
    {
        public const float OffsetMs = 200f;
        /// <summary>A death was held back: unit id, ms until its last damaging hit, ms until the death plays (smoke).</summary>
        public static event System.Action<uint, float, float> Held;

        /// <summary>Seconds to wait before the death animation of this (non-player) unit, 0 when no damaging hit on it is still pending.</summary>
        public static float Delay(HitScheduler hits, uint id)
        {
            if (hits == null) return 0f;
            float due = hits.LastDamagingDue(id), now = Time.time;
            if (due <= now) return 0f;
            float delay = due - now + OffsetMs / 1000f;
            Held?.Invoke(id, (due - now) * 1000f, delay * 1000f);
            return delay;
        }
    }
}
