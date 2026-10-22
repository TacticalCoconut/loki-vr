//! skills/npc/npcrayofgenesis.cpp: class SkillNpcRayOfGenesis : public SkillImplRecursiveDamageSplash
//! (NPC_RAYOFGENESIS).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_BLIND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNpcRayOfGenesis;

impl SkillImpl for SkillNpcRayOfGenesis {
    fn skill_id(&self) -> u16 {
        NPC_RAYOFGENESIS
    }

    crate::splash_skill_defaults!();

    /// SkillNpcRayOfGenesis::applyAdditionalEffects: Blind, 50% below level 8, 100% from level 8.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        if skill_lv < 8 {
            sc::sc_start(c, Some(src), target, SC_BLIND, 50, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        } else {
            sc::sc_start(c, Some(src), target, SC_BLIND, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        }
    }

    /// SkillNpcRayOfGenesis::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 200 * skill_lv as i32;
    }

    /// SkillNpcRayOfGenesis::castendNoDamageId: the animation, then skill_castend_damage_id on the target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillNpcRayOfGenesis {}
