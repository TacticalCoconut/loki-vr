//! skills/gunslinger/crimsonmarker.cpp: class SkillCrimsonMarker : public SkillImpl (RL_C_MARKER), rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::state::{self as pcskill, MAX_SKILL_CRIMSON_MARKER};
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_NONE;
use crate::status::defs::{SCSTART_NOAVOID, SCSTART_NORATEDEF, SCSTART_NOTICKDEF};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCrimsonMarker;

impl SkillImpl for SkillCrimsonMarker {
    fn skill_id(&self) -> u16 {
        RL_C_MARKER
    }

    /// SkillCrimsonMarker::castendNoDamageId: a player's mark taken from another caster is removed first; the target
    /// goes into the player's marker list (its slot when marked before, else the first empty one; the cast fails when
    /// all MAX_SKILL_CRIMSON_MARKER are used), then the status (val2 = the caster) and the animation. A monster's cast
    /// just starts the debuff.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let flags = SCSTART_NOAVOID | SCSTART_NOTICKDEF | SCSTART_NORATEDEF;
        let time = skill_get_time(id, skill_lv) as i64;
        if src.kind == BL_PC {
            // If marked by someone else remove it
            if type_ != SC_NONE {
                if let Some(e) = sce(c, target, type_) {
                    if e.val2 != src.id as i32 {
                        sc::status_change_end(c, target.id, type_, 0);
                    }
                }
            }
            // Check if marked before
            let marks = pcskill::get(c, src.id).c_marker;
            let slot = match (0..MAX_SKILL_CRIMSON_MARKER).find(|&i| marks.get(i).copied() == Some(target.id)) {
                Some(i) => i,
                // Find empty slot
                None => match (0..MAX_SKILL_CRIMSON_MARKER).find(|&i| marks.get(i).copied().unwrap_or(0) == 0) {
                    Some(i) => i,
                    None => {
                        skill_fail(c, src, id);
                        return;
                    }
                },
            };
            pcskill::with(c, src.id, |s| {
                if let Some(m) = s.c_marker.get_mut(slot) {
                    *m = target.id;
                }
            });
        }
        // (a monster's cast: at least SC_C_MARKER as debuff)
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::status_change_start(c, Some(src), &t, type_, 10000, skill_lv as i32, src.id as i32, 0, 0, time, flags, 0);
        let t = crate::unit::get(c, target.id).unwrap_or(t);
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
    }
}
