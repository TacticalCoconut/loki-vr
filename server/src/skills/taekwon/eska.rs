//! skills/taekwon/eska.cpp: class SkillEska : public StatusSkillImpl (SL_SKA).

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

pub struct SkillEska;

impl SkillImpl for SkillEska {
    fn skill_id(&self) -> u16 {
        SL_SKA
    }

    /// SkillEska::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let lv = skill_lv as i32;
        let is_pc = src.kind == BL_PC;
        // tsc && type != SC_NONE ? tsc->getSCE(type)
        if crate::battle::mobif::has_sc(target.kind) && type_ != crate::status::consts::SC_NONE && sc::has_sce(c, target.id, type_) {
            if is_pc {
                skill_fail(c, src, id);
            }
            sc::status_change_start(c, Some(src), src, SC_STUN, 10000, lv, 0, 0, 0, 10000, SCSTART_NORATEDEF, 0);
            sc::status_change_end(c, target.id, SC_SWOO, 0); // (rAthena ends SC_SWOO here, not the running SC_SKA)
            return;
        }
        if is_pc && !allow_es_magic_pc() && target.kind != BL_MOB {
            skill_fail(c, src, id);
            sc::status_change_start(c, Some(src), src, SC_STUN, 10000, lv, 0, 0, 0, 500, SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
            return;
        }
        status_castend_nodamage_id(c, id, false, src, target, skill_lv);
    }
}
