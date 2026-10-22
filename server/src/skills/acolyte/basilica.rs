//! skills/acolyte/basilica.cpp: class SkillBasilica : public StatusSkillImpl (HP_BASILICA).
//!
//! RENEWAL: Basilica is a status on the caster (StatusSkillImpl); castendPos2's body (the pre-renewal ground cell
//! group: cancel a running Basilica, Land Protector check, skill_unitsetting) is `#ifndef RENEWAL` and compiles to
//! nothing.

use crate::db::skills::*;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBasilica;

impl SkillImpl for SkillBasilica {
    fn skill_id(&self) -> u16 {
        HP_BASILICA
    }

    /// SkillBasilica::castendNoDamageId: RENEWAL: StatusSkillImpl::castendNoDamageId (end_if_running = false).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        status_castend_nodamage_id(c, self.skill_id(), false, src, target, skill_lv);
    }

    /// SkillBasilica::castendPos2: empty in RENEWAL (see the module doc).
    fn castend_pos2(&self, _c: &Ctx, _src: &Unit, _x: i16, _y: i16, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {}
}
