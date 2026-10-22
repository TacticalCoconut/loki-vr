//! skills/ninja/kunairotation.cpp: class SkillKunaiRotation : public SkillImpl (SS_KUNAIKAITEN), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skill::UNIT_NOCONSUME_AMMO;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillKunaiRotation;

impl SkillImpl for SkillKunaiRotation {
    fn skill_id(&self) -> u16 {
        SS_KUNAIKAITEN
    }

    /// SkillKunaiRotation::calculateSkillRatio: 1000 + 1350% per level, + 100% x level per Kunai Distortion level,
    /// + 5 x POW, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1000 + 1350 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, SS_KUNAIWAIKYOKU) * 100 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillKunaiRotation::castendPos2: the caster's status (skill_get_time2), then its own unit and a Kunai
    /// Distortion unit at (x, y), neither consuming ammunition.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), src, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_unitsetting(c, &s, id, skill_lv, x, y, UNIT_NOCONSUME_AMMO);
        let s = crate::unit::get(c, src.id).unwrap_or(s);
        skill_unitsetting(c, &s, SS_KUNAIWAIKYOKU, skill_lv, x, y, UNIT_NOCONSUME_AMMO);
    }
}
