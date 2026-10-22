//! skills/npc/speedup.cpp: class SkillSpeedUp : public SkillImpl (NPC_SPEEDUP).

use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::mob::MOB_SKILL_INTERVAL;
use crate::skills::SkillImpl;
use crate::status::consts::SCB_SPEED;
use crate::status::defs::{MIN_WALK_SPEED, SCO_NONE};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSpeedUp;

impl SkillImpl for SkillSpeedUp {
    fn skill_id(&self) -> u16 {
        NPC_SPEEDUP
    }

    /// SkillSpeedUp::castendNoDamageId: while a monster's trickcasting (NPC_RANDOMMOVE) has at least 700 ms left, its
    /// base speed goes down by 250 (not below MIN_WALK_SPEED); after that the trickcasting ends.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, tick: Tick, _flag: &mut i32) {
        // mob_data* md = BL_CAST(BL_MOB, src)
        if src.kind != BL_MOB {
            return;
        }
        let Some(mut md) = crate::mob::get(c, src.id) else { return };
        // Officially, trickcasting continues as long as there are more than 700ms left
        let trickstop: i32 = (MOB_SKILL_INTERVAL as i32 * 7) / 10;
        if md.trickcasting - tick >= trickstop as Tick {
            // This skill directly modifies a monster's base speed value
            // (md->base_status exists only for a monster with a custom base status — status_calc_mob_'s flags; the
            // Marine Sphere, the monster that has this skill, is a summon with one. rAthena dereferences it anyway: a
            // monster without one would crash the map server; here it is reported and the skill does nothing.)
            let Some(base) = md.base_status.as_mut() else {
                log::error!("NPC_SPEEDUP: monster {} ({}) has no base_status (rAthena dereferences nullptr)", md.id, md.mob_id);
                return;
            };
            base.speed = (base.speed as i32 - 250).max(MIN_WALK_SPEED) as u16;
            crate::mob::put(c, &md);
            // Need to recalc speed based on new base value
            crate::status::calc_bl(c, md.id, 1u64 << SCB_SPEED, SCO_NONE);
            // We use skills only on each full cell, to fix the inaccuracy we do this on last move interval
            let Some(mut md) = crate::mob::get(c, src.id) else { return };
            if md.trickcasting - tick < trickstop as Tick + MOB_SKILL_INTERVAL {
                md.last_skillcheck = tick + 100;
                crate::mob::put(c, &md);
            }
        } else {
            // Synchronize skill usage
            md.last_skillcheck = md.trickcasting;
            // Causes monster to stop and get ready for next alchemist skill
            md.trickcasting = 0;
            md.can_escape = false;
            crate::mob::put(c, &md);
        }
    }
}
