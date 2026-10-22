//! skills/acolyte/pneumaticusprocella.cpp: class SkillPneumaticusProcella : public SkillImpl
//! (CD_PNEUMATICUS_PROCELLA).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::status::defs::{RC_DEMON, RC_UNDEAD};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPneumaticusProcella;

impl SkillImpl for SkillPneumaticusProcella {
    fn skill_id(&self) -> u16 {
        CD_PNEUMATICUS_PROCELLA
    }

    /// SkillPneumaticusProcella::castendPos2: the cell group (skill_unitsetting; the hits are its unit's).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        crate::skill::iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillPneumaticusProcella::calculateSkillRatio: 150% + 2100% per level + 10 x SPL + 3% per Fidus Animus
    /// level; more on undead and demons (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        *skillratio += -100 + 150 + 2100 * skill_lv as i32 + 10 * sstatus.spl as i32;
        *skillratio += 3 * pc_checkskill(c, src, CD_FIDUS_ANIMUS);
        if tstatus.race as i32 == RC_UNDEAD || tstatus.race as i32 == RC_DEMON {
            *skillratio += 50 + 150 * skill_lv as i32;
            *skillratio += 2 * pc_checkskill(c, src, CD_FIDUS_ANIMUS);
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
