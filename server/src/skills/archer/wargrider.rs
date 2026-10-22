//! skills/archer/wargrider.cpp: class SkillWargRider : public SkillImpl (RA_WUGRIDER).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::defs::{OPTION_WUG, OPTION_WUGRIDER};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWargRider;

impl SkillImpl for SkillWargRider {
    fn skill_id(&self) -> u16 {
        RA_WUGRIDER
    }

    /// SkillWargRider::castendNoDamageId: a player with its warg mounts it (OPTION_WUG off, then OPTION_WUGRIDER on);
    /// a rider dismounts (OPTION_WUGRIDER off, then OPTION_WUG on); then the no-damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != crate::map::BL_PC {
            return;
        }
        // sd->sc.option as it is before each pc_setoption
        let option = |c: &Ctx| crate::unit::get(c, src.id).map(|u| u.sc.option).unwrap_or(src.sc.option);
        let o = option(c);
        let riding = o & OPTION_WUGRIDER != 0; // pc_isridingwug
        let wug = o & OPTION_WUG != 0; // pc_iswug
        if !riding && wug {
            crate::skilltree::pc_setoption(c, src.id, o & !OPTION_WUG, 0);
            crate::skilltree::pc_setoption(c, src.id, option(c) | OPTION_WUGRIDER, 0);
        } else if riding {
            crate::skilltree::pc_setoption(c, src.id, o & !OPTION_WUGRIDER, 0);
            crate::skilltree::pc_setoption(c, src.id, option(c) | OPTION_WUG, 0);
        }
        clif_skill_nodamage(c, Some(&super::fresh(c, src)), &super::fresh(c, target), self.skill_id(), skill_lv as i32, true);
    }
}
