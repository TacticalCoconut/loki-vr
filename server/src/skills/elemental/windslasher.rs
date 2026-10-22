//! skills/elemental/windslasher.cpp: class SkillWindSlasher : public SkillImpl (EL_WIND_SLASH), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::target::battle_get_master;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::consts::SC_BLEEDING;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWindSlasher;

impl SkillImpl for SkillWindSlasher {
    fn skill_id(&self) -> u16 {
        EL_WIND_SLASH
    }

    /// SkillWindSlasher::applyAdditionalEffects: a 25% chance of bleeding (val2 = the elemental's id).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        // Non confirmed rate.
        sc::sc_start2(c, Some(src), target, SC_BLEEDING, 25, skill_lv as i32, src.id as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillWindSlasher::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100;
    }

    /// SkillWindSlasher::castendDamageId: the skill animation on the master, the dummy damage packet on the target, then
    /// the attack.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let master = battle_get_master(c, src);
        clif_skill_nodamage(c, Some(src), &master, id, skill_lv as i32, true);
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }
}
