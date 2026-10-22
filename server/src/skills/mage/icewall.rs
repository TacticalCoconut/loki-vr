//! skills/mage/icewall.cpp: class SkillIceWall : public SkillImpl (WZ_ICEWALL).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_poseffect;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillIceWall;

impl SkillImpl for SkillIceWall {
    fn skill_id(&self) -> u16 {
        WZ_ICEWALL
    }

    /// SkillIceWall::castendPos2: the ground effect only when the wall was placed.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        *flag |= 1;
        if iface::skill_unitsetting(c, src, id, skill_lv, x, y, 0) {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_poseffect(c, &s, id, skill_lv, x, y, tick);
        }
    }
}
