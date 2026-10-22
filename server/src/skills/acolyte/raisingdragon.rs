//! skills/acolyte/raisingdragon.cpp: class SkillRaisingDragon : public StatusSkillImpl (SR_RAISINGDRAGON).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::state::pc_addspiritball;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::status::consts::SC_EXPLOSIONSPIRITS;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillRaisingDragon;

impl SkillImpl for SkillRaisingDragon {
    fn skill_id(&self) -> u16 {
        SR_RAISINGDRAGON
    }

    /// SkillRaisingDragon::castendNoDamageId: a player gets Fury, its spheres filled up to 5 + level (don't call
    /// more than max available spheres), then StatusSkillImpl::castendNoDamageId (the Raising Dragon status).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let max: i16 = 5 + skill_lv as i16;
        sc::sc_start(c, Some(src), target, SC_EXPLOSIONSPIRITS, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        for _ in 0..max {
            // Don't call more than max available spheres.
            pc_addspiritball(c, src.id, skill_get_time(id, skill_lv), max as i32);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        status_castend_nodamage_id(c, id, false, &s, &t, skill_lv);
    }
}
