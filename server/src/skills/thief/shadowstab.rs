//! skills/thief/shadowstab.cpp: class SkillShadowStab : public WeaponSkillImpl (SHC_SHADOW_STAB).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_CLOAKING, SC_CLOAKINGEXCEED};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillShadowStab;

impl SkillImpl for SkillShadowStab {
    fn skill_id(&self) -> u16 {
        SHC_SHADOW_STAB
    }

    /// SkillShadowStab::calculateSkillRatio (`// TODO : check pow ratio` in rAthena).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 650 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillShadowStab::castendDamageId: the caster leaves Cloaking / Cloaking Exceed, the animation, then the hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        sc::status_change_end(c, src.id, SC_CLOAKING, 0);
        sc::status_change_end(c, src.id, SC_CLOAKINGEXCEED, 0);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true);
        weapon_castend_damage_id(c, id, &s, target, skill_lv, tick, *flag);
    }
}
