//! skills/thief/masqueradeignorance.cpp: class SkillMasqueradeIgnorance : public SkillImpl (SC_IGNORANCE).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::defs::CLASS_BOSS;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMasqueradeIgnorance;

impl SkillImpl for SkillMasqueradeIgnorance {
    fn skill_id(&self) -> u16 {
        SC_IGNORANCE
    }

    /// SkillMasqueradeIgnorance::castendNoDamageId: as the other Masquerades (super::masquerade_rate); on success the
    /// target loses 100 x level SP (a monster: nothing, but the caster's half is computed from its level) and the
    /// caster gets half of it back.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let is_sd = src.kind == BL_PC;
        if sce(c, target, type_).is_none() {
            let dstmd = if target.kind == BL_MOB { crate::mob::get(c, target.id) } else { None };
            if crate::status::status_get_status_data(c, target).class_ as i32 == CLASS_BOSS {
                return;
            }
            let rate = super::masquerade_rate(c, src, target, skill_lv);
            let ok = sc::sc_start(c, Some(src), target, type_, rate, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            if clif_skill_nodamage(c, Some(&s), &t, id, 0, ok) {
                let mut sp = 100 * skill_lv as i32;
                if let Some(md) = &dstmd {
                    sp = md.level;
                }
                if dstmd.is_none() {
                    crate::status::hp::status_zap(c, target.id, 0, sp as i64, 0);
                }
                crate::status::hp::status_heal(c, src.id, 0, (sp / 2) as i64, 0, 3);
            } else if is_sd {
                skill_fail(c, src, id);
            }
        } else if is_sd {
            skill_fail(c, src, id);
        }
    }
}
