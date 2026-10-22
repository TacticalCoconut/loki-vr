//! skills/other/cheerup.cpp: class SkillCheerUp : public StatusSkillImpl (WE_CHEERUP).

use super::{pc_get_father, pc_get_mother};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::skill_fail;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCheerUp;

impl SkillImpl for SkillCheerUp {
    fn skill_id(&self) -> u16 {
        WE_CHEERUP
    }

    /// SkillCheerUp::castendNoDamageId (a player caster only): fails (requirements kept) when neither parent is
    /// online and the target is not a player; the area call gives the status (StatusSkillImpl) to the targets that
    /// are the baby's father or mother (pointer comparison: a non-player target equals an offline parent's nullptr,
    /// as in rAthena); the first call searches the players in the 7x7 splash around the target (BCT_ALL).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let dstsd = (target.kind == BL_PC).then_some(target.id);
        let f_sd = pc_get_father(c, src).map(|u| u.id);
        let m_sd = pc_get_mother(c, src).map(|u| u.id);
        if f_sd.is_none() && m_sd.is_none() && dstsd.is_none() {
            // Fail if no family members are found
            skill_fail(c, src, id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        if *flag & 1 != 0 {
            // Buff can only be given to parents in 7x7 AoE around baby
            if dstsd == f_sd || dstsd == m_sd {
                status_castend_nodamage_id(c, id, false, src, target, skill_lv);
            }
        } else {
            let f = *flag | BCT_ALL | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_PC, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        }
    }
}
