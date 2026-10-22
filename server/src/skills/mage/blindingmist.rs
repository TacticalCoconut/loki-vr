//! skills/mage/blindingmist.cpp: class SkillBlindingMist : public SkillImpl (PF_FOGWALL).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLIND, SC_DELUGE};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBlindingMist;

impl SkillImpl for SkillBlindingMist {
    fn skill_id(&self) -> u16 {
        PF_FOGWALL
    }

    /// SkillBlindingMist::applyAdditionalEffects: blinds a target other than the caster, unless it is in Deluge
    /// (`tsc == nullptr || !tsc->hasSCE(SC_DELUGE)`: a unit without a status_change has no Deluge either).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let in_deluge = crate::battle::mobif::has_sc(target.kind) && sc::has_sce(c, target.id, SC_DELUGE);
        if src.id != target.id && !in_deluge {
            sc::sc_start(c, Some(src), target, SC_BLIND, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
        }
    }

    /// SkillBlindingMist::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
