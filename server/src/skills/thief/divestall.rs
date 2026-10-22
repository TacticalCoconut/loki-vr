//! skills/thief/divestall.cpp: class SkillDivestAll : public SkillImpl (ST_FULLSTRIP).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::effect::skill_strip_equip;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CP_ARMOR, SC_CP_HELM, SC_CP_SHIELD, SC_CP_WEAPON};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDivestAll;

impl SkillImpl for SkillDivestAll {
    fn skill_id(&self) -> u16 {
        ST_FULLSTRIP
    }

    /// SkillDivestAll::castendNoDamageId: a fully chemical-protected target gets the special message; otherwise
    /// skill_strip_equip, the animation when something was stripped, a failure when nothing was.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let is_sd = src.kind == BL_PC;
        //Special message when trying to use strip on FCP [Jobbie]
        if is_sd && [SC_CP_WEAPON, SC_CP_HELM, SC_CP_ARMOR, SC_CP_SHIELD].iter().all(|&t| sce(c, target, t).is_some()) {
            crate::skillunit::clif::clif_gospel_info(c, src, 0x28);
            return;
        }
        let i = skill_strip_equip(c, src, target, id, skill_lv);
        if i {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, i);
        }
        //Nothing stripped.
        if is_sd && !i {
            skill_fail(c, src, id);
        }
    }
}
