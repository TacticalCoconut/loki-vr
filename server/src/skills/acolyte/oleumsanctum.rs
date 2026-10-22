//! skills/acolyte/oleumsanctum.cpp: class SkillOleumSanctum : public SkillImplRecursiveDamageSplash
//! (IQ_OLEUM_SANCTUM).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_HOLY_OIL;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillOleumSanctum;

impl SkillImpl for SkillOleumSanctum {
    fn skill_id(&self) -> u16 {
        IQ_OLEUM_SANCTUM
    }

    crate::splash_skill_defaults!();

    /// SkillOleumSanctum::castendNoDamageId: the animation, then the splash through skill_castend_damage_id.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_castend_damage_id(c, src, &t, id, skill_lv, tick, *flag);
    }

    /// SkillOleumSanctum::calculateSkillRatio: 500% + 2000% per level + 5 x POW (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 500 + 2000 * skill_lv as i32 + 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillOleumSanctum::applyAdditionalEffects: SC_HOLY_OIL on the target.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_HOLY_OIL, 100, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }
}

impl RecursiveDamageSplash for SkillOleumSanctum {}
