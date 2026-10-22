//! skills/swordman/hackandslasher.cpp: class SkillHackAndSlasher : public SkillImplRecursiveDamageSplash
//! (DK_HACKANDSLASHER) and class SkillHackAndSlasherAttack : public SkillImpl (DK_HACKANDSLASHER_ATK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

// ---------------------------------------------------------------------------------------------- DK_HACKANDSLASHER

pub struct SkillHackAndSlasher;

impl SkillImpl for SkillHackAndSlasher {
    fn skill_id(&self) -> u16 {
        DK_HACKANDSLASHER
    }

    crate::splash_skill_defaults!();

    /// SkillHackAndSlasher::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 500 + 1000 * skill_lv as i32;
        *skillratio += 7 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillHackAndSlasher {
    /// SkillHackAndSlasher::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}

// ---------------------------------------------------------------------------------------------- DK_HACKANDSLASHER_ATK

pub struct SkillHackAndSlasherAttack;

impl SkillImpl for SkillHackAndSlasherAttack {
    fn skill_id(&self) -> u16 {
        DK_HACKANDSLASHER_ATK
    }

    /// SkillHackAndSlasherAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 500 + 1000 * skill_lv as i32;
        *skillratio += 7 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
