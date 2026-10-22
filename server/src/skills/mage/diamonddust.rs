//! skills/mage/diamonddust.cpp: class SkillDiamondDust : public SkillImpl (SO_DIAMONDDUST).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::iface;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_COOLER_OPTION, SC_CRYSTALIZE};
use crate::status::{sc, status_get_status_data};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDiamondDust;

/// `sd ? sd->status.job_level : 0` (map_session_data* sd = BL_CAST(BL_PC, src)).
fn job_level(c: &Ctx, bl: &Unit) -> i32 {
    if bl.kind == BL_PC {
        crate::pc::character_of(c, bl.id).map_or(0, |ch| ch.job_level as i32)
    } else {
        0
    }
}

impl SkillImpl for SkillDiamondDust {
    fn skill_id(&self) -> u16 {
        SO_DIAMONDDUST
    }

    /// SkillDiamondDust::applyAdditionalEffects: SC_CRYSTALIZE at 5 + 5 * lv percent (+ job level / 5 under
    /// SC_COOLER_OPTION) for skill_get_time2.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let mut rate = 5 + 5 * skill_lv as i32;
        if sce(c, src, SC_COOLER_OPTION).is_some() {
            rate += job_level(c, src) / 5;
        }
        sc::sc_start(c, Some(src), target, SC_CRYSTALIZE, rate, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillDiamondDust::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let int_ = status_get_status_data(c, src).int_ as i32;

        *skillratio += -100 + 2 * int_ + 300 * pc_checkskill(c, src, SA_FROSTWEAPON) + int_ * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_COOLER_OPTION).is_some() {
            *skillratio += job_level(c, src) * 5;
        }
    }

    /// SkillDiamondDust::castendPos2: the skill's unit group.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
