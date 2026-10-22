//! skills/mage/frozenslash.cpp: class SkillFrozenSlash : public SkillImplRecursiveDamageSplash (AG_FROZEN_SLASH),
//! rAthena e985006.
//!
//! The cast is a castendNoDamageId (the animation, then skill_castend_damage_id: the inherited
//! SkillImplRecursiveDamageSplash::castendDamageId's splash around the target).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CLIMAX;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFrozenSlash;

impl SkillImpl for SkillFrozenSlash {
    fn skill_id(&self) -> u16 {
        AG_FROZEN_SLASH
    }

    crate::splash_skill_defaults!();

    /// SkillFrozenSlash::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;

        *skillratio += -100 + 450 + 950 * lv + 5 * sstatus.spl as i32;

        if sce(c, src, SC_CLIMAX).is_some() {
            *skillratio += 150 + 350 * lv;
        }

        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillFrozenSlash::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillFrozenSlash {}
