//! skills/archer/marionettecontrol.cpp: class SkillMarionetteControl : public SkillImpl (CG_MARIONETTE).

use crate::db::jobs::ids::{MAPID_BARDDANCER, MAPID_SECONDMASK};
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CURSE, SC_MARIONETTE, SC_MARIONETTE2, SC_QUAGMIRE};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMarionetteControl;

impl SkillImpl for SkillMarionetteControl {
    fn skill_id(&self) -> u16 {
        CG_MARIONETTE
    }

    /// SkillMarionetteControl::castendNoDamageId: refused on a Bard / Dancer of the caster's gender and on targets
    /// under Curse / Quagmire; starts SC_MARIONETTE on the caster and SC_MARIONETTE2 on the target, or ends a link
    /// between the same two; anything else fails (no requirement consumed on a failure).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let is_pc = src.kind == crate::map::BL_PC;
        let same_gender_barddancer = is_pc && target.kind == crate::map::BL_PC && {
            let dst_class = crate::status::data::sd_get(c, target.id).map(|s| s.class_).unwrap_or(0);
            let (sex, dsex) = (crate::pc::character_of(c, src.id).map(|ch| ch.sex), crate::pc::character_of(c, target.id).map(|ch| ch.sex));
            dst_class & MAPID_SECONDMASK == MAPID_BARDDANCER && dsex.is_some() && dsex == sex
        };
        if same_gender_barddancer || sce(c, target, SC_CURSE).is_some() || sce(c, target, SC_QUAGMIRE).is_some() {
            // Cannot cast on another bard/dancer-type class of the same gender as caster, or targets under Curse/Quagmire
            if is_pc {
                skill_fail(c, src, id);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        // if (sc && tsc)
        if !crate::battle::mobif::has_sc(src.kind) || !crate::battle::mobif::has_sc(target.kind) {
            return;
        }
        let m1 = sce(c, src, SC_MARIONETTE);
        let m2 = sce(c, target, SC_MARIONETTE2);
        let time = skill_get_time(id, skill_lv) as i64;
        if m1.is_none() && m2.is_none() {
            sc::sc_start(c, Some(src), src, SC_MARIONETTE, 100, target.id as i32, time);
            let s = super::fresh(c, src);
            sc::sc_start(c, Some(&s), target, SC_MARIONETTE2, 100, src.id as i32, time);
            clif_skill_nodamage(c, Some(&super::fresh(c, src)), &super::fresh(c, target), id, skill_lv as i32, true);
        } else if m1.map(|e| e.val1 == target.id as i32).unwrap_or(false) && m2.map(|e| e.val1 == src.id as i32).unwrap_or(false) {
            sc::status_change_end(c, src.id, SC_MARIONETTE, 0);
            sc::status_change_end(c, target.id, SC_MARIONETTE2, 0);
        } else {
            if is_pc {
                skill_fail(c, src, id);
            }
            *flag |= SKILL_NOCONSUME_REQ;
        }
    }
}
