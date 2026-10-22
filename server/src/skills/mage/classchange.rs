//! skills/mage/classchange.cpp: class SkillClassChange : public SkillImpl (SA_CLASSCHANGE).

use crate::db::mobs::{MOBG_CLASSCHANGE, RMF_DB_RATE};
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::defs::MD_STATUSIMMUNE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillClassChange;

impl SkillImpl for SkillClassChange {
    fn skill_id(&self) -> u16 {
        SA_CLASSCHANGE
    }

    /// SkillClassChange::castendNoDamageId: a monster target becomes a random monster of MOBG_CLASSCHANGE
    /// (mob_get_random_id(MOBG_CLASSCHANGE, RMF_DB_RATE, 0), then mob_class_change).
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
        let class_ = crate::mob::spawn::get_random_id(c, MOBG_CLASSCHANGE, RMF_DB_RATE, 0);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        crate::mob::dead::mob_class_change(c, target.id, class_);
        classchange_end_statuses(c, target);
    }
}

/// classchange.cpp / monocell.cpp (the same lines in both): `if (tsc && status_has_mode(&dstmd->status,
/// MD_STATUSIMMUNE))` after mob_class_change (the new class' status) ends the common ailments SC_COMMON_MIN ..
/// SC_COMMON_MAX, then these ten statuses, each when present.
pub(super) fn classchange_end_statuses(c: &Ctx, target: &Unit) {
    let Some(dstmd) = crate::mob::get(c, target.id) else { return };
    if !crate::mob::has_mode(&dstmd, MD_STATUSIMMUNE) {
        return;
    }
    const SCS: [i32; 10] = [SC_QUAGMIRE, SC_PROVOKE, SC_ROKISWEIL, SC_GRAVITATION, SC_SUITON, SC_STRIPWEAPON, SC_STRIPSHIELD, SC_STRIPARMOR, SC_STRIPHELM, SC_BLADESTOP];
    for i in SC_COMMON_MIN..=SC_COMMON_MAX {
        if sc::has_sce(c, target.id, i) {
            sc::status_change_end(c, target.id, i, 0);
        }
    }
    for t in SCS {
        if sc::has_sce(c, target.id, t) {
            sc::status_change_end(c, target.id, t, 0);
        }
    }
}
