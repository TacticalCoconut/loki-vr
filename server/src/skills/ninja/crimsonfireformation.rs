//! skills/ninja/crimsonfireformation.cpp: class SkillCrimsonFireFormation : public SkillImpl (NJ_KAENSIN), rAthena
//! e985006.

use super::charms_of_type;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skill::state::CHARM_TYPE_FIRE;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCrimsonFireFormation;

impl SkillImpl for SkillCrimsonFireFormation {
    fn skill_id(&self) -> u16 {
        NJ_KAENSIN
    }

    /// SkillCrimsonFireFormation::calculateSkillRatio: -50%, + 20% per fire charm.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio -= 50;
        let charms = charms_of_type(c, src, CHARM_TYPE_FIRE);
        if charms > 0 {
            *base_skillratio += 20 * charms;
        }
    }

    /// SkillCrimsonFireFormation::castendPos2: flag 1 keeps the ammo (it is deleted when the group is).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
