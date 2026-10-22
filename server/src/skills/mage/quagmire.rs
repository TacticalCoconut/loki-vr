//! skills/mage/quagmire.cpp: class SkillQuagmire : public SkillImpl (WZ_QUAGMIRE). SC_QUAGMIRE is started by its
//! skill unit (skillunit/effect.rs).

use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillQuagmire;

impl SkillImpl for SkillQuagmire {
    fn skill_id(&self) -> u16 {
        WZ_QUAGMIRE
    }

    /// SkillQuagmire::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
