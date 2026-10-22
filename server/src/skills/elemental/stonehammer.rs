//! skills/elemental/stonehammer.cpp: class SkillStoneHammer : public SkillImpl (EL_STONE_HAMMER), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::target::battle_get_master;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStoneHammer;

impl SkillImpl for SkillStoneHammer {
    fn skill_id(&self) -> u16 {
        EL_STONE_HAMMER
    }

    /// SkillStoneHammer::applyAdditionalEffects: a 10% per level chance to stun.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 10 * skill_lv as i32, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillStoneHammer::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 400;
    }

    /// SkillStoneHammer::castendDamageId: the skill animation on the master, the dummy damage packet on the target, then
    /// the attack.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let master = battle_get_master(c, src);
        clif_skill_nodamage(c, Some(src), &master, id, skill_lv as i32, true);
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }
}
