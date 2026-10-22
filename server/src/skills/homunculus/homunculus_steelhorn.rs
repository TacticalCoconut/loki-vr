//! skills/homunculus/homunculus_steelhorn.cpp: class SkillSteelHorn : public SkillImpl (MH_STAHL_HORN).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_GOLDENE_FERSE, SC_STUN};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSteelHorn;

impl SkillImpl for SkillSteelHorn {
    fn skill_id(&self) -> u16 {
        MH_STAHL_HORN
    }

    /// SkillSteelHorn::castendDamageId: a hit of the skill's own type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillSteelHorn::calculateSkillRatio: -100 + 1000 + 300 * lv * base level / 150 + VIT (!TODO upstream: confirm
    /// the VIT bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 1000 + 300 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 150 + sstatus.vit as i32;
    }

    /// SkillSteelHorn::applyAdditionalEffects: SC_STUN at 20 + 2% per level.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, SC_STUN, 20 + 2 * skill_lv as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }

    /// SkillSteelHorn::modifyElement: holy while the caster has SC_GOLDENE_FERSE.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if sce(c, src, SC_GOLDENE_FERSE).is_some() {
            *element = crate::status::defs::ELE_HOLY;
        }
    }
}
