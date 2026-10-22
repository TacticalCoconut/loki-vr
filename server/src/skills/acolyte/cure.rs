//! skills/acolyte/cure.cpp: class SkillCure : public SkillImpl (AL_CURE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BITESCAR, SC_BLIND, SC_CONFUSION, SC_SILENCE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCure;

impl SkillImpl for SkillCure {
    fn skill_id(&self) -> u16 {
        AL_CURE
    }

    /// SkillCure::castendNoDamageId: ends Silence, Blind, Confusion and Bite Scar (not on an immune target).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if sc::status_isimmune(c, bl) != 0 {
            clif_skill_nodamage(c, Some(src), bl, id, skill_lv as i32, false);
            return;
        }
        sc::status_change_end(c, bl.id, SC_SILENCE, 0);
        sc::status_change_end(c, bl.id, SC_BLIND, 0);
        sc::status_change_end(c, bl.id, SC_CONFUSION, 0);
        sc::status_change_end(c, bl.id, SC_BITESCAR, 0);
        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
    }
}
