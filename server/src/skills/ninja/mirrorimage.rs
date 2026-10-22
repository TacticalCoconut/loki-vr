//! skills/ninja/mirrorimage.cpp: class SkillMirrorImage : public StatusSkillImpl (NJ_BUNSINJYUTSU), rAthena e985006.

use crate::db::skills::*;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::status::consts::{SC_BUNSINJYUTSU, SC_NEN};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMirrorImage;

impl SkillImpl for SkillMirrorImage {
    fn skill_id(&self) -> u16 {
        NJ_BUNSINJYUTSU
    }

    /// SkillMirrorImage::castendNoDamageId: recasting ends the running Mirror Image first (on official recasting
    /// cancels existing mirror image [helvetica]), then StatusSkillImpl's start, then Ninja Aura ends.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // TODO: refactor into status.yml (rAthena's note)
        sc::status_change_end(c, target.id, SC_BUNSINJYUTSU, 0);
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        status_castend_nodamage_id(c, self.skill_id(), false, &s, &t, skill_lv);
        sc::status_change_end(c, target.id, SC_NEN, 0);
    }
}
