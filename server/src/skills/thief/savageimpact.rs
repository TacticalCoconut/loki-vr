//! skills/thief/savageimpact.cpp: class SkillSavageImpact : public SkillImplRecursiveDamageSplash (SHC_SAVAGE_IMPACT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::path::{DIRX, DIRY, DIR_NORTHEAST};
use crate::skill::area;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_CLOAKINGEXCEED, SC_SHADOW_EXCEED};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSavageImpact;

impl SkillImpl for SkillSavageImpact {
    fn skill_id(&self) -> u16 {
        SHC_SAVAGE_IMPACT
    }

    crate::splash_skill_defaults!();

    /// SkillSavageImpact::modifyDamageData: `dmg.div_ = dmg.div_ + dmg.miscflag` (miscflag 2 after Cloaking Exceed,
    /// see splashSearch).
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        dmg.div_ = (dmg.div_ as i32 + dmg.miscflag) as i16;
    }

    /// SkillSavageImpact::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 130 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        if sce(c, src, SC_SHADOW_EXCEED).is_some() {
            *skillratio += 30 * lv;
            *skillratio += 2 * sstatus.pow as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillSavageImpact {
    /// SkillSavageImpact::splashSearch: out of Cloaking Exceed the hits get the +2 (skill_area_temp[0] = 2, which the
    /// recursive calls pass as the attack's flag); move the player 1 cell near the target, between the target and
    /// the player; the animation, then the splash.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        if sce(c, src, SC_CLOAKINGEXCEED).is_some() {
            area::set_temp(0, 2);
            sc::status_change_end(c, src.id, SC_CLOAKINGEXCEED, 0);
        }
        let src = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let mut dir = DIR_NORTHEAST; // up-right when src is on the same cell of target
        if target.x != src.x || target.y != src.y {
            dir = crate::map::calc_dir(target, src.x, src.y); // dir based on target as we move player based on target location
        }
        if skill_check_unit_movepos(c, 5, &src, target.x + DIRX[dir as usize], target.y + DIRY[dir as usize], 0, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
