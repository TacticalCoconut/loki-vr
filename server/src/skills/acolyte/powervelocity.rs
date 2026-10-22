//! skills/acolyte/powervelocity.cpp: class SkillPowerVelocity : public SkillImpl (SR_POWERVELOCITY).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state::{self, pc_addspiritball, pc_delspiritball};
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillPowerVelocity;

impl SkillImpl for SkillPowerVelocity {
    fn skill_id(&self) -> u16 {
        SR_POWERVELOCITY
    }

    /// SkillPowerVelocity::castendNoDamageId: a player target with at most 5 spheres gets spheres from the caster.
    /// The loop is kept as rAthena has it: six rounds of pc_addspiritball(target, Call Spirits' duration at the
    /// caster's level, max i) for i = 0..5, each followed by pc_delspiritball(caster, all its spheres).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if target.kind != BL_PC {
            return;
        }
        if src.kind == BL_PC && state::get(c, target.id).spiritball <= 5 {
            let time = skill_get_time(MO_CALLSPIRITS, pc_checkskill(c, src, MO_CALLSPIRITS) as u16);
            for i in 0..=5 {
                pc_addspiritball(c, target.id, time, i);
                pc_delspiritball(c, src.id, state::get(c, src.id).spiritball as i32, 0);
            }
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
    }
}
