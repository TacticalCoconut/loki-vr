//! skills/mercenary/mercenary_decreaseagi.cpp: class SkillMercenaryDecreaseAgi : public SkillImpl (MER_DECAGI).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryDecreaseAgi;

impl SkillImpl for SkillMercenaryDecreaseAgi {
    fn skill_id(&self) -> u16 {
        MER_DECAGI
    }

    /// SkillMercenaryDecreaseAgi::castendNoDamageId: rate 50 + 3 * skill_lv + (caster level + caster INT) / 5.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let sstatus = crate::status::status_get_status_data(c, src);
        let type_ = skill_get_sc(id) as i32;
        let rate = 50 + skill_lv as i32 * 3 + (crate::status::status_get_lv(c, src) + sstatus.int_ as i32) / 5;
        let ok = sc::sc_start(c, Some(src), target, type_, rate, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
