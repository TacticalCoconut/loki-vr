//! skills/ninja/kunairefraction.cpp: class SkillKunaiRefraction : public SkillImpl (SS_KUNAIKUSSETSU), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_SKILL;
use crate::skill::area::{foreach_in_range, Wall};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::skillunit::ontimer::skill_detonator;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillKunaiRefraction;

impl SkillImpl for SkillKunaiRefraction {
    fn skill_id(&self) -> u16 {
        SS_KUNAIKUSSETSU
    }

    /// SkillKunaiRefraction::calculateSkillRatio: 300 + 450% per level, + 10% x level per Kunai Rotation level, + 5 x
    /// POW, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 300 + 450 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, SS_KUNAIKAITEN) * 10 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillKunaiRefraction::castendPos2: skill_detonator on the caster's skill units in the splash range (its Kunai
    /// Distortion units turn into Kunai Refraction units of this level), then the animation.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_SKILL, Wall::None, |c, bl| skill_detonator(c, bl, src, skill_lv as i32));
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
    }
}
