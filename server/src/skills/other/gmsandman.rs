//! skills/other/gmsandman.cpp: class SkillGmSandman : public SkillImpl (GM_SANDMAN).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::defs::OPT1_SLEEP;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillGmSandman;

impl SkillImpl for SkillGmSandman {
    fn skill_id(&self) -> u16 {
        GM_SANDMAN
    }

    /// SkillGmSandman::castendNoDamageId: toggles the target's opt1 between none and OPT1_SLEEP (no status change),
    /// sends the option change to the area, then the packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // status_change *tsc = status_get_sc(target); if( tsc )
        if !crate::battle::mobif::has_sc(target.kind) {
            return;
        }
        let Some(mut t) = unit::get(c, target.id) else { return };
        if t.sc.opt1 == OPT1_SLEEP {
            t.sc.opt1 = 0;
        } else {
            t.sc.opt1 = OPT1_SLEEP;
        }
        unit::save(c, &t);
        crate::clif::changeoption(c, &t, t.sc.option, None);
        clif_skill_nodamage(c, Some(src), &t, self.skill_id(), skill_lv as i32, true);
    }
}
