//! skills/swordman/banding.cpp: class SkillBanding : public SkillImpl (LG_BANDING).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::iface;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_BANDING;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillBanding;

impl SkillImpl for SkillBanding {
    fn skill_id(&self) -> u16 {
        LG_BANDING
    }

    /// SkillBanding::castendPos2: a running Banding ends; else the unit group goes on the caster's cell and
    /// SC_BANDING keeps its group id (val4). The packet is sent either way.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if sce(c, src, SC_BANDING).is_some() {
            sc::status_change_end(c, src.id, SC_BANDING, 0);
        } else if let Some(group_id) = iface::skill_unitsetting_group_id(c, src, id, skill_lv, src.x, src.y, 0) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            sc::sc_start4(c, Some(&s), &s, SC_BANDING, 100, skill_lv as i32, 0, 0, group_id, skill_get_time(id, skill_lv) as i64);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
    }
}
