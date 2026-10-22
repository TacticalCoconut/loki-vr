//! skills/npc/grounddrive.cpp: class SkillGroundDrive : public SkillImpl (NPC_GROUNDDRIVE), rAthena e985006. Its
//! hits come from its skill unit (skill_unitsetting: skillunit agent).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGroundDrive;

impl SkillImpl for SkillGroundDrive {
    fn skill_id(&self) -> u16 {
        NPC_GROUNDDRIVE
    }

    /// SkillGroundDrive::calculateSkillRatio (RENEWAL: +25%).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 25;
    }

    /// SkillGroundDrive::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
