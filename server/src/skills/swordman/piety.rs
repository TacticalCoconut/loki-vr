//! skills/swordman/piety.cpp: class SkillPiety : public SkillImpl (LG_PIETY).

use crate::battle::{BCT_PARTY, BCT_SELF};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_PREAMBLE;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillPiety;

impl SkillImpl for SkillPiety {
    fn skill_id(&self) -> u16 {
        LG_PIETY
    }

    /// SkillPiety::castendNoDamageId: the status on the caster's party members (and the caster) around the target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        } else {
            area::set_temp(2, 0);
            let f = *flag | SD_PREAMBLE | BCT_PARTY | BCT_SELF | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_PC, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        }
    }
}
