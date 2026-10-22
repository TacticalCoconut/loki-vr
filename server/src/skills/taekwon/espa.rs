//! skills/taekwon/espa.cpp: class SkillEspa : public SkillImpl (SP_SPA).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::attack::skill_attack;
use crate::skill::clif::skill_fail;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::taekwon::allow_es_magic_pc;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_STUN, SC_USE_SKILL_SP_SPA};
use crate::status::defs::{SCSTART_NORATEDEF, SCSTART_NOTICKDEF};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEspa;

impl SkillImpl for SkillEspa {
    fn skill_id(&self) -> u16 {
        SP_SPA
    }

    /// SkillEspa::applyCounterAdditionalEffects.
    fn apply_counter_additional_effects(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: &mut i32) {
        sc::sc_start(c, Some(src), src, SC_USE_SKILL_SP_SPA, 100, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillEspa::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 400 + 250 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillEspa::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC && !allow_es_magic_pc() && target.kind != BL_MOB {
            sc::status_change_start(c, Some(src), src, SC_STUN, 10000, skill_lv as i32, 0, 0, 0, 500, SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
            skill_fail(c, src, id);
            return;
        }
        skill_attack(c, BF_MAGIC, src, src, target, id, skill_lv, tick, *flag);
    }
}
