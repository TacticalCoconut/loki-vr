//! skills/swordman/shieldspell.cpp: class SkillShieldSpell : public SkillImpl (LG_SHIELDSPELL).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_SHIELDSPELL_ATK, SC_SHIELDSPELL_HP, SC_SHIELDSPELL_SP};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillShieldSpell;

impl SkillImpl for SkillShieldSpell {
    fn skill_id(&self) -> u16 {
        LG_SHIELDSPELL
    }

    /// SkillShieldSpell::castendNoDamageId: level 1 HP, level 2 SP, higher levels ATK.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = if skill_lv == 1 {
            SC_SHIELDSPELL_HP
        } else if skill_lv == 2 {
            SC_SHIELDSPELL_SP
        } else {
            SC_SHIELDSPELL_ATK
        };
        let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
