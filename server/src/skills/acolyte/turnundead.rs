//! skills/acolyte/turnundead.cpp: class SkillTurnUndead : public SkillImpl (PR_TURNUNDEAD).

use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::status::sc::battle_check_undead;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTurnUndead;

impl SkillImpl for SkillTurnUndead {
    fn skill_id(&self) -> u16 {
        PR_TURNUNDEAD
    }

    /// SkillTurnUndead::castendDamageId: only undead targets (the instant kill chance is battle_calc_magic_attack's).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if !battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
            return;
        }
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
