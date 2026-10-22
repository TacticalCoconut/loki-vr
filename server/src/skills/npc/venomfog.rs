//! skills/npc/venomfog.cpp: class SkillVenomFog : public SkillImpl (NPC_VENOMFOG).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillVenomFog;

impl SkillImpl for SkillVenomFog {
    fn skill_id(&self) -> u16 {
        NPC_VENOMFOG
    }

    /// SkillVenomFog::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 600 + 100 * skill_lv as i32;
    }

    /// SkillVenomFog::castendPos2: the skill's ground unit (skill_unitsetting: skillunit agent, skill/iface.rs).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
