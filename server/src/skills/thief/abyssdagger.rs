//! skills/thief/abyssdagger.cpp: class SkillAbyssDagger : public SkillImplRecursiveDamageSplash (ABC_ABYSS_DAGGER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAbyssDagger;

impl SkillImpl for SkillAbyssDagger {
    fn skill_id(&self) -> u16 {
        ABC_ABYSS_DAGGER
    }

    crate::splash_skill_defaults!();

    /// SkillAbyssDagger::castendNoDamageId: the status on the target, the animation, then the damage part
    /// (skill_castend_damage_id: the recursive splash).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_castend_damage_id(c, &s, &t, id, skill_lv, tick, *flag);
    }

    /// SkillAbyssDagger::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 350 + 1400 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillAbyssDagger {}
