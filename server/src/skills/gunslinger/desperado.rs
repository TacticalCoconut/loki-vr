//! skills/gunslinger/desperado.cpp: class SkillDesperado : public SkillImpl (GS_DESPERADO), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_FALLEN_ANGEL;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDesperado;

impl SkillImpl for SkillDesperado {
    fn skill_id(&self) -> u16 {
        GS_DESPERADO
    }

    /// SkillDesperado::calculateSkillRatio: + 50% per level above 1, doubled under Fallen Angel.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50 * (skill_lv as i32 - 1);
        if sce(c, src, SC_FALLEN_ANGEL).is_some() {
            *base_skillratio *= 2;
        }
    }

    /// SkillDesperado::castendPos2: flag 1 keeps the ammo (it is deleted when the group is).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
