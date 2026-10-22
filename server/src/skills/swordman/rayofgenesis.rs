//! skills/swordman/rayofgenesis.cpp: class SkillRayOfGenesis : public SkillImplRecursiveDamageSplash (LG_RAYOFGENESIS).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_BLIND, SC_INSPIRATION};
use crate::status::defs::{ELE_NEUTRAL, RC_DEMON};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRayOfGenesis;

impl SkillImpl for SkillRayOfGenesis {
    fn skill_id(&self) -> u16 {
        LG_RAYOFGENESIS
    }

    crate::splash_skill_defaults!();

    /// SkillRayOfGenesis::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillRayOfGenesis::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 350 * skill_lv as i32;
        *skillratio += sstatus.int_ as i32 * 3;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillRayOfGenesis::applyAdditionalEffects: 50% chance to cause Blind on Undead and Demon monsters.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) || tstatus.race as i32 == RC_DEMON {
            sc::sc_start(c, Some(src), target, SC_BLIND, 50, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
        }
    }

    /// SkillRayOfGenesis::modifyElement: neutral under Inspiration.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if sce(c, src, SC_INSPIRATION).is_some() {
            *element = ELE_NEUTRAL;
        }
    }
}

impl RecursiveDamageSplash for SkillRayOfGenesis {}
