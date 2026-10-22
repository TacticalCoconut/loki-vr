//! skills/npc/fullheal.cpp: class SkillFullHeal : public SkillImpl (NPC_ALLHEAL), rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFullHeal;

impl SkillImpl for SkillFullHeal {
    fn skill_id(&self) -> u16 {
        NPC_ALLHEAL
    }

    /// SkillFullHeal::castendNoDamageId: the target's HP is fully restored (shown as Heal); a monster forgets who
    /// damaged it.
    fn castend_nodamage_id(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if sc::status_isimmune(c, target) != 0 {
            return;
        }
        // mob_data* dstmd = BL_CAST(BL_MOB, target)
        let dstmd = target.kind == BL_MOB;
        let heal = crate::status::hp::status_percent_heal(c, target.id, 100, 0, 0);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, None, &t, AL_HEAL, heal, true);
        if dstmd {
            // Reset Damage Logs
            if let Some(mut md) = crate::mob::get(c, target.id) {
                md.dmglog.clear();
                crate::mob::put(c, &md);
            }
        }
    }
}
