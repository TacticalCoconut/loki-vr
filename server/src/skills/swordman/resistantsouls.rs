//! skills/swordman/resistantsouls.cpp: class SkillResistantSouls : public SkillImpl (CR_PROVIDENCE).

use crate::db::jobs::{MAPID_CRUSADER, MAPID_SECONDMASK};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillResistantSouls;

impl SkillImpl for SkillResistantSouls {
    fn skill_id(&self) -> u16 {
        CR_PROVIDENCE
    }

    /// SkillResistantSouls::castendNoDamageId: a player may not cast it on another crusader [Skotlex].
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC && target.kind == BL_PC {
            // (dstsd->class_ & MAPID_SECONDMASK) == MAPID_CRUSADER
            if let Some(dsd) = crate::status::data::sd_get(c, target.id) {
                if dsd.class_ & MAPID_SECONDMASK == MAPID_CRUSADER {
                    skill_fail(c, src, id);
                    return;
                }
            }
        }
        let ok = sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
