//! skills/taekwon/kaupe.cpp: class SkillKaupe : public StatusSkillImpl (SL_KAUPE).

use crate::db::skills::*;
use crate::skill::clif::skill_fail;
use crate::skills::taekwon::soul_linker_target_ok;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::status::consts::SC_STUN;
use crate::status::defs::SCSTART_NORATEDEF;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillKaupe;

impl SkillImpl for SkillKaupe {
    fn skill_id(&self) -> u16 {
        SL_KAUPE
    }

    /// SkillKaupe::castendNoDamageId: a player caster may only target the players of the soul linker rule.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if !soul_linker_target_ok(c, src, target, false) {
            sc::status_change_start(c, Some(src), src, SC_STUN, 10000, skill_lv as i32, 0, 0, 0, 500, SCSTART_NORATEDEF, 0);
            skill_fail(c, src, id);
            return;
        }
        status_castend_nodamage_id(c, id, false, src, target, skill_lv);
    }
}
