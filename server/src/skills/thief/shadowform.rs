//! skills/thief/shadowform.cpp: class SkillShadowForm : public SkillImpl (SC_SHADOWFORM).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillShadowForm;

impl SkillImpl for SkillShadowForm {
    fn skill_id(&self) -> u16 {
        SC_SHADOWFORM
    }

    /// SkillShadowForm::castendNoDamageId: a player on another player that nobody shadows yet: SC__SHADOWFORM on the
    /// caster (val2 = the target, val3 = 4 + level hits), and on success the target's shadowform_id is the caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let is_sd = src.kind == BL_PC;
        let dstsd = target.kind == BL_PC;
        if is_sd && dstsd && src.id != target.id && crate::skill::state::get(c, target.id).shadowform_id == 0 {
            let ok = sc::sc_start4(c, Some(src), src, type_, 100, skill_lv as i32, target.id as i32, 4 + skill_lv as i32, 0, skill_get_time(id, skill_lv) as i64);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            if clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, ok) {
                crate::skill::state::with(c, target.id, |st| st.shadowform_id = src.id);
            }
        } else if is_sd {
            skill_fail(c, src, id);
        }
    }
}
