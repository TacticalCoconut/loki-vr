//! skills/summoner/catnipmeteor.cpp: class SkillCatnipMeteor : public SkillImpl (SU_CN_METEOR) and class
//! SkillCatnipMeteor2 : public SkillImpl (SU_CN_METEOR2).

use super::colors_of_hyun_rok_element;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::iface;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, repeat_count};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CURSE, SC_DORAM_SVSP};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// The calculateSkillRatio both classes have (the same body).
fn catnip_ratio(c: &Ctx, src: &Unit, skill_lv: u16, skillratio: &mut i32) {
    let sstatus = crate::status::status_get_status_data(c, src);
    *skillratio += -100 + 200 + 100 * skill_lv as i32;
    if crate::status::status_get_lv(c, src) > 99 {
        *skillratio += sstatus.int_ as i32 * 5;
    }
    re_lvl_dmod(c, src, skillratio, 100);
}

// SU_CN_METEOR
pub struct SkillCatnipMeteor;

impl SkillImpl for SkillCatnipMeteor {
    fn skill_id(&self) -> u16 {
        SU_CN_METEOR
    }

    /// SkillCatnipMeteor::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        catnip_ratio(c, src, skill_lv, skillratio);
    }

    /// SkillCatnipMeteor::castendPos2: a player holding the skill's first required item casts SU_CN_METEOR2 instead
    /// ("FIX ME: missing check of required item": SU_CN_METEOR has no ItemCost, so itemid[0] is 0 and
    /// pc_search_inventory(sd, 0) answers the first empty inventory slot); Spirit of Land starts SC_DORAM_SVSP; then
    /// one unit on a random cell of the splash area per unit interval of the skill's duration.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let mut skill_id = self.skill_id();
        if src.kind == BL_PC {
            let item = skill_db().find(SU_CN_METEOR).map(|s| s.require.itemid[0]).unwrap_or(0);
            if crate::inventory::pc_search_inventory(c, src.id, item) >= 0 {
                skill_id = SU_CN_METEOR2;
            }
            if pc_checkskill(c, src, SU_SPIRITOFLAND) != 0 {
                sc::sc_start(c, Some(src), src, SC_DORAM_SVSP, 100, 100, skill_get_time(SU_SPIRITOFLAND, 1) as i64);
            }
        }
        let area = skill_get_splash(skill_id, skill_lv);
        let interval = skill_get_unit_interval(skill_id);
        for i in 1..=repeat_count(skill_get_time(skill_id, skill_lv), interval) {
            // Creates a random Cell in the Splash Area (int32 math stored in the int16 tmpx / tmpy)
            let tmpx = (x as i32 - area + (c.rnd() % (area * 2 + 1) as u32) as i32) as i16;
            let tmpy = (y as i32 - area + (c.rnd() % (area * 2 + 1) as u32) as i32) as i16;
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            iface::skill_unitsetting(c, &s, skill_id, skill_lv, tmpx, tmpy, *flag + i * interval);
        }
    }

    /// SkillCatnipMeteor::modifyElement.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        colors_of_hyun_rok_element(c, src, element);
    }
}

// SU_CN_METEOR2
pub struct SkillCatnipMeteor2;

impl SkillImpl for SkillCatnipMeteor2 {
    fn skill_id(&self) -> u16 {
        SU_CN_METEOR2
    }

    /// SkillCatnipMeteor2::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_CURSE, 20, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillCatnipMeteor2::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        catnip_ratio(c, src, skill_lv, skillratio);
    }

    /// SkillCatnipMeteor2::modifyElement.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        colors_of_hyun_rok_element(c, src, element);
    }
}
