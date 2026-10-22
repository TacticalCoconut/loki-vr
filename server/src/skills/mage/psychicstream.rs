//! skills/mage/psychicstream.cpp: class SkillPsychicStream : public SkillImplRecursiveDamageSplash (EM_PSYCHIC_STREAM),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::path::{DIRX, DIRY, DIR_NORTHEAST};
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL};
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillPsychicStream;

impl SkillImpl for SkillPsychicStream {
    fn skill_id(&self) -> u16 {
        EM_PSYCHIC_STREAM
    }

    crate::splash_skill_defaults!();

    /// SkillPsychicStream::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);

        *skillratio += -100 + 1750 + 3850 * skill_lv as i32;
        *skillratio += 12 * sstatus.spl as i32;

        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillPsychicStream {
    /// SkillPsychicStream::splashSearch: the caster dashes next to the target (on its side facing the caster) and hits
    /// it, then SkillImplRecursiveDamageSplash::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src);
        let is_pc = src.kind == BL_PC;
        let mut dir = DIR_NORTHEAST;

        if target.x != src.x || target.y != src.y {
            dir = crate::map::calc_dir(target, src.x, src.y); // dir based on target as we move player based on target location
        }

        // (int16 + int16 → int, passed as skill_check_unit_movepos's int16)
        let dx = (target.x as i32 + DIRX[dir as usize] as i32) as i16;
        let dy = (target.y as i32 + DIRY[dir as usize] as i32) as i16;
        if skill_check_unit_movepos(c, 0, src, dx, dy, 1, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
            skill_attack(c, BF_MAGIC, &s, &s, target, id, skill_lv, tick, flag);
            let s = unit::get(c, src.id).unwrap_or_else(|| s.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        } else {
            if is_pc {
                clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
            }
            // TODO: Should we return here?
        }

        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        splash_search_default(self, c, &s, &t, skill_lv, tick, flag);
    }
}
