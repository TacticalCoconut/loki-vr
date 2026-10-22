//! skills/merchant/armcannon.cpp: class SkillArmCannon : public SkillImplRecursiveDamageSplash (NC_ARMSCANNON).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_ABR_DUAL_CANNON;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillArmCannon::SkillArmCannon() : SkillImplRecursiveDamageSplash(NC_ARMSCANNON).
pub struct SkillArmCannon;

impl SkillImpl for SkillArmCannon {
    fn skill_id(&self) -> u16 {
        NC_ARMSCANNON
    }
    crate::splash_skill_defaults!();

    /// modifyDamageData: `dmg.div_ = 2` while the caster has SC_ABR_DUAL_CANNON.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if crate::skills::novice::sce(c, src, SC_ABR_DUAL_CANNON).is_some() {
            dmg.div_ = 2;
        }
    }

    /// SkillArmCannon::calculateSkillRatio: 400% + 350% per level, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 400 + 350 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillArmCannon::modifyElement: a player's arrow element while it attacks with arrows (sd->state.arrow_atk).
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if src.kind == BL_PC && crate::battle::state::get(c, src.id).arrow_atk {
            if let Some(sd) = crate::status::data::sd_get(c, src.id) {
                *element = sd.bonus.arrow_ele;
            }
        }
    }
}

impl RecursiveDamageSplash for SkillArmCannon {
    /// SkillArmCannon::getSplashTarget: splash_target(src).
    fn get_splash_target(&self, _c: &Ctx, src: &Unit) -> u16 {
        crate::skill::area::splash_target(src)
    }

    /// SkillArmCannon::splashSearch: skill_area_temp[1] = 0, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        crate::skill::area::set_temp(1, 0);
        crate::skills::splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
