//! skills/taekwon/esma.cpp: class SkillEsma : public SkillImpl (SL_SMA).

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

pub struct SkillEsma;

impl SkillImpl for SkillEsma {
    fn skill_id(&self) -> u16 {
        SL_SMA
    }

    /// SkillEsma::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let _ = (target, skill_lv);
        // Base damage is 40% + lv%
        *base_skillratio += -60 + crate::status::status_get_lv(c, src);
    }

    /// SkillEsma::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        sc::status_change_end(c, src.id, SC_SMA, 0);
        let src = &crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        if src.kind == BL_PC && !allow_es_magic_pc() && target.kind != BL_MOB {
            sc::status_change_start(c, Some(src), src, SC_STUN, 10000, skill_lv as i32, 0, 0, 0, 500, SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
            skill_fail(c, src, id);
            return;
        }
        skill_attack(c, BF_MAGIC, src, src, target, id, skill_lv, tick, *flag);
    }
}
