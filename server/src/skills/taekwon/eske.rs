//! skills/taekwon/eske.cpp: class SkillEske : public StatusSkillImpl (SL_SKE).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::skill_fail;
use crate::skills::taekwon::allow_es_magic_pc;
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};
use crate::skills::{status_castend_nodamage_id, SkillImpl};

pub struct SkillEske;

impl SkillImpl for SkillEske {
    fn skill_id(&self) -> u16 {
        SL_SKE
    }

    /// SkillEske::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        if src.kind == BL_PC && !allow_es_magic_pc() && target.kind != BL_MOB {
            skill_fail(c, src, id);
            sc::status_change_start(c, Some(src), src, SC_STUN, 10000, lv, 0, 0, 0, 500, SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
            return;
        }
        status_castend_nodamage_id(c, id, false, src, target, skill_lv);
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, SC_SMA, 100, lv, skill_get_time(SL_SMA, skill_lv) as i64);
    }
}
