//! skills/ninja/coldbloodedcannon.cpp: class SkillColdBloodedCannon : public SkillImpl (SS_REIKETSUHOU), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_WOS;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_WATER_CHARM_POWER;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillColdBloodedCannon;

impl SkillImpl for SkillColdBloodedCannon {
    fn skill_id(&self) -> u16 {
        SS_REIKETSUHOU
    }

    /// SkillColdBloodedCannon::calculateSkillRatio: 450 + 950% per level, + 40% x level per Darkening Cannon level,
    /// + 5 x SPL, + 7000% under SC_WATER_CHARM_POWER, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 450 + 950 * skill_lv as i32;
        *skillratio += 40 * pc_checkskill(c, src, SS_ANTENPOU) * skill_lv as i32;
        *skillratio += 5 * sstatus.spl as i32;
        if sce(c, src, SC_WATER_CHARM_POWER).is_some() {
            *skillratio += 7000;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillColdBloodedCannon::castendDamageId: the splash hit (flag & 1).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::splash_hit(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillColdBloodedCannon::castendPos2: the mirages cast Darkening Cannon; on a Land Protector cell the cast fails
    /// (requirements kept); else the square around (x, y) is hit (map_foreachinallarea, flag | 1).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        super::skill_mirage_cast(c, src, None, SS_ANTENPOU, skill_lv, 0, 0, tick, *flag | BCT_WOS);
        if crate::skill::iface::map_getcell_landprotector(c, src.map, x, y) {
            if src.kind == BL_PC {
                clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        super::pos_splash(c, id, src, x, y, skill_lv, tick, *flag);
    }
}
