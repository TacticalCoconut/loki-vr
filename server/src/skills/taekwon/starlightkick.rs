//! skills/taekwon/starlightkick.cpp: class SkillStarLightKick : public SkillImplRecursiveDamageSplash (SKE_STAR_LIGHT_KICK).

use crate::battle::calc::Damage;
use crate::battle::clif::clif_blown;
use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::path::{DIRX, DIRY, DIR_NORTHEAST};
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL};
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillStarLightKick;

impl SkillImpl for SkillStarLightKick {
    fn skill_id(&self) -> u16 {
        SKE_STAR_LIGHT_KICK
    }

    crate::splash_skill_defaults!();

    /// SkillStarLightKick::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 400 + 200 * lv;
        *skillratio += lv * 5 * pc_checkskill(c, src, SKE_SKY_MASTERY);
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillStarLightKick {
    /// SkillStarLightKick::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        let mut dir = DIR_NORTHEAST;
        if target.x != src.x || target.y != src.y {
            dir = crate::map::calc_dir(target, src.x, src.y); // dir based on target as we move player based on target location
        }
        if skill_check_unit_movepos(c, 0, src, target.x + DIRX[dir as usize], target.y + DIRY[dir as usize], 1, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true);
            clif_blown(c, &s);
            skill_attack(c, BF_WEAPON, &s, &s, target, id, skill_lv, tick, flag);
        } else {
            // map_session_data* sd = BL_CAST(BL_PC, src)
            if src.kind == BL_PC {
                clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
            }
            // TODO: Should we return here?
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
        splash_search_default(self, c, &s, &t, skill_lv, tick, flag);
    }
}
