//! skills/acolyte/magnusexorcismus.cpp: class SkillMagnusExorcismus : public SkillImpl (PR_MAGNUS).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::defs::RC_DEMON;
use crate::status::sc::battle_check_undead;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMagnusExorcismus;

impl SkillImpl for SkillMagnusExorcismus {
    fn skill_id(&self) -> u16 {
        PR_MAGNUS
    }

    /// SkillMagnusExorcismus::castendPos2: the Magnus Exorcismus cell group (skill_unitsetting; the hits are its
    /// unit's skill_unit_onplace_timer).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        crate::skill::iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillMagnusExorcismus::calculateSkillRatio: +30% on undead and demons.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, _src: &Unit, target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) || tstatus.race as i32 == RC_DEMON {
            *base_skillratio += 30;
        }
    }
}
