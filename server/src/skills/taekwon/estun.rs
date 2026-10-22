//! skills/taekwon/estun.cpp: class SkillEstun : public SkillImpl (SL_STUN).

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::skill::attack::skill_attack;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::skill_fail;
use crate::skills::taekwon::allow_es_magic_pc;
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};
use crate::skills::SkillImpl;

pub struct SkillEstun;

impl SkillImpl for SkillEstun {
    fn skill_id(&self) -> u16 {
        SL_STUN
    }

    /// SkillEstun::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let _ = (c, src, target);
        *base_skillratio += 5 * skill_lv as i32;
    }

    /// SkillEstun::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC && !allow_es_magic_pc() && target.kind != BL_MOB {
            sc::status_change_start(c, Some(src), src, SC_STUN, 10000, skill_lv as i32, 0, 0, 0, 500, SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
            skill_fail(c, src, id);
            return;
        }
        skill_attack(c, BF_MAGIC, src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillEstun::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if tstatus.size as i32 == SZ_MEDIUM {
            //Only stuns mid-sized mobs.
            sc::sc_start(c, Some(src), target, SC_STUN, 30 + 10 * skill_lv as i32, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
        }
    }
}
