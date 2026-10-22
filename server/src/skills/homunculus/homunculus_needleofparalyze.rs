//! skills/homunculus/homunculus_needleofparalyze.cpp: class SkillNeedleOfParalyze : public SkillImpl
//! (MH_NEEDLE_OF_PARALYZE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::consts::SC_PARALYSIS;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNeedleOfParalyze;

impl SkillImpl for SkillNeedleOfParalyze {
    fn skill_id(&self) -> u16 {
        MH_NEEDLE_OF_PARALYZE
    }

    /// SkillNeedleOfParalyze::castendDamageId: a hit of the skill's own type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillNeedleOfParalyze::calculateSkillRatio: -100 + 450 * lv * base level / 100 + DEX (!TODO upstream: confirm
    /// the base level and DEX bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 450 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.dex as i32;
    }

    /// SkillNeedleOfParalyze::applyAdditionalEffects: SC_PARALYSIS at 30 + 5% per level.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, SC_PARALYSIS, 30 + 5 * skill_lv as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
