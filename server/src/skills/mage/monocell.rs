//! skills/mage/monocell.cpp: class SkillMonocell : public SkillImpl (SA_MONOCELL).

use crate::db::mobs::MOBID_PORING;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::defs::MD_STATUSIMMUNE;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMonocell;

impl SkillImpl for SkillMonocell {
    fn skill_id(&self) -> u16 {
        SA_MONOCELL
    }

    /// SkillMonocell::castendNoDamageId: a monster target becomes a Poring (mob_class_change(dstmd, MOBID_PORING)).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if target.kind != BL_MOB {
            return;
        }
        let Some(dstmd) = crate::mob::get(c, target.id) else { return };
        if src.kind == BL_PC && crate::mob::has_mode(&dstmd, MD_STATUSIMMUNE) {
            skill_fail(c, src, id);
            return;
        }
        let class_ = MOBID_PORING as i32;
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        crate::mob::dead::mob_class_change(c, target.id, class_);
        // the status ends after the change: the same lines as classchange.cpp
        super::classchange::classchange_end_statuses(c, target);
    }
}
