//! skills/acolyte/snap.cpp: class SkillSnap : public SkillImpl (MO_BODYRELOCATION).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSnap;

impl SkillImpl for SkillSnap {
    fn skill_id(&self) -> u16 {
        MO_BODYRELOCATION
    }

    /// SkillSnap::castendPos2: the instant move (unit_movepos easy 2), its packet (PACKETVER >= 20111005:
    /// clif_snap), and Asura Strike blocked for 2 s.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if unit::movepos(c, src.id, x, y, 2, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::skill::clif::clif_snap(c, &s, s.x, s.y);
            if src.kind == BL_PC {
                crate::skill::cooldown::skill_blockpc_start(c, src.id, MO_EXTREMITYFIST, 2000);
            }
        }
    }
}
