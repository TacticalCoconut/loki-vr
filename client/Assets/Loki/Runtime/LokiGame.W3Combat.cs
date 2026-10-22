using Loki.Game;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 3, w3combat (design notes; re-audit §2e-§2g): one schedule for everything a hit does. ZC_NOTIFY_ACT3
    /// (EntityManager) and ZC_NOTIFY_SKILL2 (CombatFx) hand their damage to <see cref="W3CombatHits"/>, which lands each hit at its own time
    /// with its number, the target's flinch, the hit sound, EF_HIT1, arrows and monster projectiles (Game/HitSchedule.cs); the
    /// controller rumble listens here. Entities.Damage / Combat.SkillDamage stay the packet-arrival events (companion assist, smokes).
    /// </summary>
    public sealed partial class LokiGame
    {
        public HitScheduler W3CombatHits { get; private set; }
        /// <summary>Rumble requests made by landed hits (the device may be absent: VR.Haptics.Sent counts the delivered ones).</summary>
        public int W3CombatHapticRequests { get; private set; }

        private void BeginW3Combat()
        {
            W3CombatHits = gameObject.AddComponent<HitScheduler>();
            W3CombatHits.Init(Entities, Effects, _boot.Assets, Audio);
            Entities.Hits = W3CombatHits; Combat.Hits = W3CombatHits;
            W3CombatHits.Landed += W3CombatHitLanded;
        }

        /// <summary>The rumble of a hit, when its number shows (the same amplitudes as before, now once per hit): a hit on us shakes
        /// both hands, a hit we deal pulses the main hand.</summary>
        private void W3CombatHitLanded(ScheduledHit h)
        {
            if (h.Amount <= 0 || h.Has(HitFlags.Miss) || h.Has(HitFlags.Lucky)) return;
            bool crit = h.Has(HitFlags.Crit);
            uint self = Session != null ? Session.AccountId : 0;
            if (self == 0) return;
            if (h.DstId == self) { VR.Haptics.Both(VR.Haptics.Kind.HitTaken, crit ? 0.6f : 0.4f, 0.08f); W3CombatHapticRequests++; } // kinds: client wave 4 (w4vr)
            else if (h.SrcId == self) { VR.Haptics.Pulse(VR.Haptics.Kind.HitDealt, true, crit ? 0.45f : 0.25f, 0.04f); W3CombatHapticRequests++; }
        }
    }
}
