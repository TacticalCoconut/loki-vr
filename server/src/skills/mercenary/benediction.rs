//! skills/mercenary/mercenary_benediction.cpp: class SkillMercenaryBenediction : public SkillImpl (MER_BENEDICTION).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLIND, SC_CURSE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryBenediction;

impl SkillImpl for SkillMercenaryBenediction {
    fn skill_id(&self) -> u16 {
        MER_BENEDICTION
    }

    /// SkillMercenaryBenediction::castendNoDamageId: cures Curse and Blind.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        sc::status_change_end(c, target.id, SC_CURSE, 0);
        sc::status_change_end(c, target.id, SC_BLIND, 0);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, self.skill_id(), skill_lv as i32, true);
    }
}
