//! skills/swordman/ignitionbreak.cpp: class SkillIgnitionBreak : public SkillImplRecursiveDamageSplash (RK_IGNITIONBREAK).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillIgnitionBreak;

impl SkillImpl for SkillIgnitionBreak {
    fn skill_id(&self) -> u16 {
        RK_IGNITIONBREAK
    }

    crate::splash_skill_defaults!();

    /// SkillIgnitionBreak::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 450 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillIgnitionBreak::castendNoDamageId (PACKETVER >= 20180207: the skill packet, not the dummy damage one).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        area::set_temp(1, 0);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
        foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
    }
}

impl RecursiveDamageSplash for SkillIgnitionBreak {}
