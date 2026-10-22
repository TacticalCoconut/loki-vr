//! skills/swordman/fightingspirit.cpp: class SkillFightingSpirit : public SkillImpl (RK_FIGHTINGSPIRIT).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFightingSpirit;

impl SkillImpl for SkillFightingSpirit {
    fn skill_id(&self) -> u16 {
        RK_FIGHTINGSPIRIT
    }

    /// SkillFightingSpirit::castendNoDamageId: val1 ATK bonus 70 + 7 * Rune Mastery level (!TODO in rAthena: confirm
    /// the new ATK formula), val2 ASPD boost 4 * Rune Mastery level; non-players count Rune Mastery's max level.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        // uint8 runemastery_skill_lv = (sd ? pc_checkskill(sd, RK_RUNEMASTERY) : skill_get_max(RK_RUNEMASTERY))
        let runemastery_skill_lv = (if src.kind == BL_PC { pc_checkskill(c, src, RK_RUNEMASTERY) } else { skill_get_max(RK_RUNEMASTERY) }) as u8 as i32;
        sc::sc_start2(c, Some(src), target, skill_get_sc(id) as i32, 100, 70 + 7 * runemastery_skill_lv, 4 * runemastery_skill_lv, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
    }
}
