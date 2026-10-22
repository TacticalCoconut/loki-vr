//! skills/mage/whirlwind.cpp: class SkillWhirlwind : public SkillImpl (SA_VIOLENTGALE).

use crate::db::skills::*;
use crate::skill::{iface, SKILL_NOCONSUME_REQ};
use crate::skills::SkillImpl;
use crate::skillunit::group::skill_locate_element_field;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWhirlwind;

impl SkillImpl for SkillWhirlwind {
    fn skill_id(&self) -> u16 {
        SA_VIOLENTGALE
    }

    /// SkillWhirlwind::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        //Does not consumes if the skill is already active. [Skotlex]
        if let Some(sg2) = skill_locate_element_field(c, src.id).filter(|g| matches!(g.skill_id, SA_VOLCANO | SA_DELUGE | SA_VIOLENTGALE)) {
            // sg2->limit - DIFF_TICK(gettick(), sg2->tick) > 0
            if sg2.limit - (c.tick() - sg2.tick) > 0 {
                iface::skill_unitsetting(c, src, id, skill_lv, x, y, 0);
                *flag |= SKILL_NOCONSUME_REQ; // not to consume items
                return;
            } else {
                crate::skillunit::data::upd_group(c, sg2.group_id, |g| g.limit = 0); //Disable it.
            }
        }
        iface::skill_unitsetting(c, src, id, skill_lv, x, y, 0);
    }
}
