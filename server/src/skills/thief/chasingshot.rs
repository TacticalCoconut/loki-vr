//! skills/thief/chasingshot.cpp: class SkillChasingShot : public SkillImplRecursiveDamageSplash (ABC_CHASING_SHOT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::path::{DIRX, DIRY, DIR_NORTHEAST};
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CHASING;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillChasingShot;

impl SkillImpl for SkillChasingShot {
    fn skill_id(&self) -> u16 {
        ABC_CHASING_SHOT
    }

    crate::splash_skill_defaults!();

    /// SkillChasingShot::modifyDamageData: 3 hits under SC_CHASING.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if sce(c, src, SC_CHASING).is_some() {
            dmg.div_ = 3;
        }
    }

    /// SkillChasingShot::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1750 + 850 * lv;
        *skillratio += 5 * sstatus.con as i32;
        if sce(c, src, SC_CHASING).is_some() {
            *skillratio += 250 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillChasingShot {
    /// SkillChasingShot::splashSearch: the caster jumps next to the target (on its side towards the caster), the
    /// animation, then the splash.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let mut dir = DIR_NORTHEAST;
        if target.x != src.x || target.y != src.y {
            dir = crate::map::calc_dir(target, src.x, src.y);
        }
        if skill_check_unit_movepos(c, 0, src, target.x + DIRX[dir as usize], target.y + DIRY[dir as usize], 1, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
