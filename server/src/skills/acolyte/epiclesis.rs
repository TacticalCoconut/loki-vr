//! skills/acolyte/epiclesis.cpp: class SkillEpiclesis : public SkillImpl (AB_EPICLESIS).

use crate::battle::BCT_NOENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEpiclesis;

impl SkillImpl for SkillEpiclesis {
    fn skill_id(&self) -> u16 {
        AB_EPICLESIS
    }

    /// SkillEpiclesis::castendPos2: the tree (skill_unitsetting); once placed, Resurrection level 1 on every
    /// non-enemy in the splash area (map_foreachinallarea).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if crate::skill::iface::skill_unitsetting(c, src, id, skill_lv, x, y, 0) {
            let i = skill_get_splash(id, skill_lv) as i16;
            let f = *flag | BCT_NOENEMY | 1;
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            foreach_in_area(c, s.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, &s, ALL_RESURRECTION, 1, tick, f, skill_castend_nodamage_id));
        }
    }
}
