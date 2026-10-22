//! skills/homunculus/homunculus_continualbreakcombo.cpp: class SkillContinualBreakCombo : public SkillImpl (MH_CBC).

use super::{fresh, yommy_duration};
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::SC_CBC;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillContinualBreakCombo;

impl SkillImpl for SkillContinualBreakCombo {
    fn skill_id(&self) -> u16 {
        MH_CBC
    }

    /// SkillContinualBreakCombo::castendDamageId: SC_CBC on the target for the Yommy formula's duration (the skill
    /// packet with its result), then the hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let duration = yommy_duration(c, src, target, skill_lv); // Yommy formula
        let ok = sc::sc_start4(c, Some(src), target, SC_CBC, 100, skill_lv as i32, src.id as i32, 0, 0, duration as i64);
        let t = fresh(c, target);
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
        let (s, t) = (fresh(c, src), fresh(c, target));
        crate::skill::attack::skill_attack(c, skill_get_type(id), &s, &s, &t, id, skill_lv, tick, *flag);
    }
}
