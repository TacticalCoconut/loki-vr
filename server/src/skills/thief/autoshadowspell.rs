//! skills/thief/autoshadowspell.cpp: class SkillAutoShadowSpell : public SkillImpl (SC_AUTOSHADOWSPELL).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::fail::USESKILL_FAIL_IMITATION_SKILL_NONE;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage};
use crate::skills::SkillImpl;
use crate::status::consts::SC_STOP;
use crate::status::sc::{self, INFINITE_TICK};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAutoShadowSpell;

impl SkillImpl for SkillAutoShadowSpell {
    fn skill_id(&self) -> u16 {
        SC_AUTOSHADOWSPELL
    }

    /// SkillAutoShadowSpell::castendNoDamageId: with a plagiarised or reproduced skill in its slot, SC_STOP (val1 = the
    /// skill level, read back by skill_select_menu) and the selection list; else the "no imitated skill" failure.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        let id = self.skill_id();
        let skills = crate::skilltree::Skills::load(c, src.id);
        // (sd->reproduceskill_idx > 0 && sd->status.skill[sd->reproduceskill_idx].id) || (the same for cloneskill_idx)
        let has = |idx: u16| idx > 0 && skills.as_ref().map(|s| s.at(idx).id != 0).unwrap_or(false);
        let (r, cl) = skills.as_ref().map(|s| (s.meta.reproduceskill_idx, s.meta.cloneskill_idx)).unwrap_or((0, 0));
        if has(r) || has(cl) {
            sc::sc_start(c, Some(src), src, SC_STOP, 100, skill_lv as i32, INFINITE_TICK); // The skill_lv is stored in val1 used in skill_select_menu to determine the used skill lvl [Xazax]
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            super::ext::clif_autoshadowspell_list(c, &s);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, 1, true);
        } else {
            clif_skill_fail(c, src, id, USESKILL_FAIL_IMITATION_SKILL_NONE, 0, 0);
        }
    }
}
