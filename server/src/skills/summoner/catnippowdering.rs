//! skills/summoner/catnippowdering.cpp: class SkillCatnipPowdering : public SkillImpl (SU_CN_POWDERING).

use super::sd_status;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::consts::SC_DORAM_FLEE2;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCatnipPowdering;

impl SkillImpl for SkillCatnipPowdering {
    fn skill_id(&self) -> u16 {
        SU_CN_POWDERING
    }

    /// SkillCatnipPowdering::castendPos2: Spirit of Land starts SC_DORAM_FLEE2 (base level * 10 / 12), then the unit.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        if let Some(ch) = sd_status(c, src) {
            if pc_checkskill(c, src, SU_SPIRITOFLAND) != 0 {
                sc::sc_start(c, Some(src), src, SC_DORAM_FLEE2, 100, ch.base_level as i32 * 10 / 12, skill_get_time(SU_SPIRITOFLAND, 1) as i64);
            }
        }
        *flag |= 1;
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        iface::skill_unitsetting(c, &s, self.skill_id(), skill_lv, x, y, 0);
    }
}
