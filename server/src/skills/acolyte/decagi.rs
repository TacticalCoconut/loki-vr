//! skills/acolyte/decagi.cpp: class SkillDecreaseAgi : public SkillImpl (AL_DECAGI).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::{sc, status_get_lv};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDecreaseAgi;

impl SkillImpl for SkillDecreaseAgi {
    fn skill_id(&self) -> u16 {
        AL_DECAGI
    }

    /// SkillDecreaseAgi::castendNoDamageId: rate 50 + 3 * level + (caster level + INT) / 5.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let sstatus = crate::status::status_get_status_data(c, src);
        let rate = 50 + skill_lv as i32 * 3 + (status_get_lv(c, src) + sstatus.int_ as i32) / 5;
        // (sc_start is evaluated before the packet it is an argument of)
        let ok = sc::sc_start(c, Some(src), bl, type_, rate, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
