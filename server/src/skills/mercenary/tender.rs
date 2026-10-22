//! skills/mercenary/mercenary_tender.cpp: class SkillMercenaryTender : public SkillImpl (MER_TENDER).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_FREEZE, SC_STONE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryTender;

impl SkillImpl for SkillMercenaryTender {
    fn skill_id(&self) -> u16 {
        MER_TENDER
    }

    /// SkillMercenaryTender::castendNoDamageId: cures Freeze and Stone.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        sc::status_change_end(c, target.id, SC_FREEZE, 0);
        sc::status_change_end(c, target.id, SC_STONE, 0);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, self.skill_id(), skill_lv as i32, true);
    }
}
