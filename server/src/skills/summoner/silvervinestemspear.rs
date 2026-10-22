//! skills/summoner/silvervinestemspear.cpp: class SkillSilvervineStemSpear : public SkillImpl (SU_SV_STEMSPEAR).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLEEDING, SC_DORAM_WALKSPEED};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSilvervineStemSpear;

impl SkillImpl for SkillSilvervineStemSpear {
    fn skill_id(&self) -> u16 {
        SU_SV_STEMSPEAR
    }

    /// SkillSilvervineStemSpear::applyAdditionalEffects: Bleeding (10 %, val2 the caster, skill_get_time2).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start2(c, Some(src), target, SC_BLEEDING, 10, skill_lv as i32, src.id as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillSilvervineStemSpear::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 600;
    }

    /// SkillSilvervineStemSpear::castendDamageId: Spirit of Land starts SC_DORAM_WALKSPEED (val1 50), then the attack
    /// of the skill's type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC && pc_checkskill(c, src, SU_SPIRITOFLAND) != 0 {
            sc::sc_start(c, Some(src), src, SC_DORAM_WALKSPEED, 100, 50, skill_get_time(SU_SPIRITOFLAND, 1) as i64);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_attack(c, skill_get_type(id), &s, &s, target, id, skill_lv, tick, *flag);
    }
}
