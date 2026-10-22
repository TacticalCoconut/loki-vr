//! skills/thief/antidote.cpp: class SkillAntidote : public SkillImpl (GC_ANTIDOTE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillAntidote;

impl SkillImpl for SkillAntidote {
    fn skill_id(&self) -> u16 {
        GC_ANTIDOTE
    }

    /// SkillAntidote::castendNoDamageId: the animation, then the Guillotine poisons end on a target with statuses.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        // if( tsc ): status_get_sc(target)
        if crate::battle::mobif::has_sc(target.kind) {
            for t in [SC_PARALYSE, SC_PYREXIA, SC_DEATHHURT, SC_LEECHESEND, SC_VENOMBLEED, SC_MAGICMUSHROOM, SC_TOXIN, SC_OBLIVIONCURSE] {
                sc::status_change_end(c, target.id, t, 0);
            }
        }
    }
}
