//! skills/thief/fromtheabyss.cpp: class SkillFromTheAbyss : public SkillImpl (ABC_FROM_THE_ABYSS) and class
//! SkillFromTheAbyssAttack : public SkillImplRecursiveDamageSplash (ABC_FROM_THE_ABYSS_ATK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFromTheAbyss;

impl SkillImpl for SkillFromTheAbyss {
    fn skill_id(&self) -> u16 {
        ABC_FROM_THE_ABYSS
    }

    /// SkillFromTheAbyss::castendNoDamageId: the status (val2 = the caster), then the animation with its result.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let ok = sc::sc_start2(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, src.id as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, ok);
    }
}

pub struct SkillFromTheAbyssAttack;

impl SkillImpl for SkillFromTheAbyssAttack {
    fn skill_id(&self) -> u16 {
        ABC_FROM_THE_ABYSS_ATK
    }

    crate::splash_skill_defaults!();

    /// SkillFromTheAbyssAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 150 + 650 * skill_lv as i32;
        *skillratio += 5 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillFromTheAbyssAttack {}
