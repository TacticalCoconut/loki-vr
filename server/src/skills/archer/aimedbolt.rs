//! skills/archer/aimedbolt.cpp: class SkillAimedBolt : public WeaponSkillImpl (RA_AIMEDBOLT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::status::consts::SC_FEARBREEZE;

pub struct SkillAimedBolt;

impl SkillImpl for SkillAimedBolt {
    fn skill_id(&self) -> u16 {
        RA_AIMEDBOLT
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillAimedBolt::calculateSkillRatio: under Fear Breeze -100 + 800 + 35 * lv, else -100 + 500 + 20 * lv; RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if sce(c, src, SC_FEARBREEZE).is_some() {
            *skillratio += -100 + 800 + 35 * skill_lv as i32;
        } else {
            *skillratio += -100 + 500 + 20 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
