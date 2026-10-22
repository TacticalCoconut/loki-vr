//! skills/thief/crossslash.cpp: class SkillCrossSlash : public SkillImplRecursiveDamageSplash (SHC_CROSS_SLASH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_SHADOW_EXCEED;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCrossSlash;

impl SkillImpl for SkillCrossSlash {
    fn skill_id(&self) -> u16 {
        SHC_CROSS_SLASH
    }

    crate::splash_skill_defaults!();

    /// SkillCrossSlash::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 300 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        if sce(c, src, SC_SHADOW_EXCEED).is_some() {
            *skillratio += 60 * lv;
            *skillratio += 2 * sstatus.pow as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillCrossSlash {
    /// SkillCrossSlash::splashSearch: the animation, the caster's status, then the splash.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or(s);
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
