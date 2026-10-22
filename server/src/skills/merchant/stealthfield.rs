//! skills/merchant/stealthfield.cpp: class SkillStealthField : public SkillImpl (NC_STEALTHFIELD).

use crate::db::skills::*;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::skillunit::skill_clear_unitgroup;
use crate::status::consts::SC_STEALTHFIELD_MASTER;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillStealthField::SkillStealthField() : SkillImpl(NC_STEALTHFIELD).
pub struct SkillStealthField;

impl SkillImpl for SkillStealthField {
    fn skill_id(&self) -> u16 {
        NC_STEALTHFIELD
    }

    /// SkillStealthField::castendPos2: a second cast while SC_STEALTHFIELD_MASTER runs removes the caster's unit groups (requirements
    /// kept); otherwise the caster's groups are removed ("To remove previous skills - cannot used combined"), the
    /// unit is placed on the caster's cell and SC_STEALTHFIELD_MASTER starts with its group id.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if sce(c, src, SC_STEALTHFIELD_MASTER).is_some() {
            skill_clear_unitgroup(c, src.id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        skill_clear_unitgroup(c, src.id);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        if let Some(group_id) = crate::skillunit::skill_unitsetting(c, &s, id, skill_lv, s.x, s.y, 0) {
            let s = unit::get(c, src.id).unwrap_or(s);
            sc::sc_start2(c, Some(&s), &s, SC_STEALTHFIELD_MASTER, 100, skill_lv as i32, group_id, skill_get_time(id, skill_lv) as i64);
        }
    }
}
