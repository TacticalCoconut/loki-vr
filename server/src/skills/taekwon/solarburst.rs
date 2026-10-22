//! skills/taekwon/solarburst.cpp: class SkillSolarBurst : public SkillImplRecursiveDamageSplash (SJ_SOLARBURST).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_LIGHTOFSUN;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSolarBurst;

impl SkillImpl for SkillSolarBurst {
    fn skill_id(&self) -> u16 {
        SJ_SOLARBURST
    }

    crate::splash_skill_defaults!();

    /// SkillSolarBurst::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 900 + 220 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if let Some(e) = sce(c, src, SC_LIGHTOFSUN) {
            *skillratio += *skillratio * e.val2 / 100;
        }
    }

    /// SkillSolarBurst::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillSolarBurst {}
