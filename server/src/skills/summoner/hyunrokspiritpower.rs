//! skills/summoner/hyunrokspiritpower.cpp: class SkillHyunrokSpiritPower : public SkillImplRecursiveDamageSplash
//! (SH_HYUN_ROK_SPIRIT_POWER).

use super::colors_of_hyun_rok_element;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHyunrokSpiritPower;

impl SkillImpl for SkillHyunrokSpiritPower {
    fn skill_id(&self) -> u16 {
        SH_HYUN_ROK_SPIRIT_POWER
    }

    crate::splash_skill_defaults!();

    /// SkillHyunrokSpiritPower::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 350 + 200 * skill_lv as i32;
        *skillratio += 30 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        *skillratio += 5 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHyunrokSpiritPower::modifyElement.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        colors_of_hyun_rok_element(c, src, element);
    }
}

impl RecursiveDamageSplash for SkillHyunrokSpiritPower {
    /// SkillHyunrokSpiritPower::splashSearch: the skill packet on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
