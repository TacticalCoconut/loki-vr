//! skills/mage/endowtornado.cpp: class SkillEndowTornado : public SkillImpl (SA_LIGHTNINGLOADER).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::defs::W_FIST;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEndowTornado;

impl SkillImpl for SkillEndowTornado {
    fn skill_id(&self) -> u16 {
        SA_LIGHTNINGLOADER
    }

    /// SkillEndowTornado::castendNoDamageId (RENEWAL): a player target without a weapon fails; otherwise the endow
    /// starts at 100%. (`#else`: the pre-renewal 60% + 10%/lv chance and weapon unequip are not compiled.)
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // dstsd && dstsd->status.weapon == W_FIST
        if target.kind == BL_PC && crate::pc::character_of(c, target.id).map(|ch| ch.weapon as i32 == W_FIST).unwrap_or(false) {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, false);
            return;
        }
        let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
