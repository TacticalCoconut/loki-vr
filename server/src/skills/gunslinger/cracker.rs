//! skills/gunslinger/cracker.cpp: class SkillCracker : public SkillImpl (GS_CRACKER), rAthena e985006.

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCracker;

impl SkillImpl for SkillCracker {
    fn skill_id(&self) -> u16 {
        GS_CRACKER
    }

    /// SkillCracker::castendNoDamageId: per official standards, this skill works on players and mobs; a player's cast
    /// stuns with 65 - 5% per cell of distance (at least 30%).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC && (target.kind == BL_PC || target.kind == BL_MOB) {
            let mut i = 65 - 5 * crate::mob::unit::distance_bl(src, target); // Base rate
            if i < 30 {
                i = 30;
            }
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start(c, Some(src), &t, SC_STUN, i, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        }
    }
}
