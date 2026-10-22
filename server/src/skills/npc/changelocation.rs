//! skills/npc/changelocation.cpp: class SkillChangeLocation : public SkillImpl (NPC_MOVE_COORDINATE), rAthena
//! e985006.

use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skill::misc::skill_check_unit_movepos;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::defs::CLASS_BOSS;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillChangeLocation;

impl SkillImpl for SkillChangeLocation {
    fn skill_id(&self) -> u16 {
        NPC_MOVE_COORDINATE
    }

    /// SkillChangeLocation::castendNoDamageId: the target comes to the caster's cell; a caster that is not a boss
    /// takes the target's former cell.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let (px, py) = (target.x, target.y);
        if !skill_check_unit_movepos(c, 0, target, src.x, src.y, 1, true) {
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or(t);
        clif_skill_damage(c, &s, &t, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        let t = unit::get(c, target.id).unwrap_or(t);
        crate::battle::clif::clif_blown(c, &t);
        // If caster is not a boss, switch coordinates with the target
        if crate::battle::bl::class_type(c, &s) != CLASS_BOSS {
            if !skill_check_unit_movepos(c, 0, &s, px, py, 1, true) {
                *flag |= SKILL_NOCONSUME_REQ;
                return;
            }
            let s = unit::get(c, src.id).unwrap_or(s);
            crate::battle::clif::clif_blown(c, &s);
        }
    }
}
