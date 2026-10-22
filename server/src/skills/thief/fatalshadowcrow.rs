//! skills/thief/fatalshadowcrow.cpp: class SkillFatalShadowCrow : public SkillImplRecursiveDamageSplash
//! (SHC_FATAL_SHADOW_CROW).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::path::{DIRX, DIRY, DIR_NORTHEAST};
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_DARKCROW;
use crate::status::defs::{RC_DEMIHUMAN, RC_DRAGON};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFatalShadowCrow;

impl SkillImpl for SkillFatalShadowCrow {
    fn skill_id(&self) -> u16 {
        SHC_FATAL_SHADOW_CROW
    }

    crate::splash_skill_defaults!();

    /// SkillFatalShadowCrow::applyAdditionalEffects: SC_DARKCROW at the caster's Dark Claw level (at least 1).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let lv = 1.max(pc_checkskill(c, src, GC_DARKCROW));
        sc::sc_start(c, Some(src), target, SC_DARKCROW, 100, lv, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillFatalShadowCrow::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1300 * lv + 10 * sstatus.pow as i32;
        if tstatus.race as i32 == RC_DEMIHUMAN || tstatus.race as i32 == RC_DRAGON {
            *skillratio += 150 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillFatalShadowCrow {
    /// SkillFatalShadowCrow::splashSearch: move the player 1 cell near the target, between the target and the
    /// player, trigger the animation, then the splash.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let mut dir = DIR_NORTHEAST;
        if target.x != src.x || target.y != src.y {
            dir = crate::map::calc_dir(target, src.x, src.y); // dir based on target as we move player based on target location
        }
        if skill_check_unit_movepos(c, 5, src, target.x + DIRX[dir as usize], target.y + DIRY[dir as usize], 0, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), target, self.skill_id(), skill_lv as i32, true); // Trigger animation
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
