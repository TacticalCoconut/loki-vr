//! skills/homunculus/homunculus_sonicclaw.cpp: class SkillSonicClaw : public SkillImpl (MH_SONIC_CRAW).

use super::hd_of;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSonicClaw;

impl SkillImpl for SkillSonicClaw {
    fn skill_id(&self) -> u16 {
        MH_SONIC_CRAW
    }

    /// SkillSonicClaw::modifyDamageData: one hit per spirit sphere the homunculus has (after skill_isNotOk_hom took
    /// the skill's own).
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if let Some(hd) = hd_of(c, src) {
            dmg.div_ = hd.homunculus.spiritball as i16;
        }
    }

    /// SkillSonicClaw::castendDamageId: a hit of the skill's own type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillSonicClaw::calculateSkillRatio: -100 + 60 * lv * base level / 150.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 60 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 150;
    }
}
