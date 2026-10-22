//! skills/mage/siennaexecrate.cpp: class SkillSiennaExecrate : public SkillImpl (WL_SIENNAEXECRATE), rAthena e985006.
//! The first target rolls the success chance; on success the skill's status (skill_get_sc: SC_STONEWAIT) spreads to
//! the enemies around it without a roll (flag & 1).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::status::defs::SCSTART_NOTICKDEF;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSiennaExecrate;

impl SkillImpl for SkillSiennaExecrate {
    fn skill_id(&self) -> u16 {
        WL_SIENNAEXECRATE
    }

    /// SkillSiennaExecrate::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // status_change *tsc = status_get_sc(target)
        let tsc = crate::battle::mobif::has_sc(target.kind);
        if sc::status_isimmune(c, target) != 0 || !tsc {
            return;
        }
        if *flag & 1 != 0 {
            if target.id as i32 == area::temp(1) {
                return; // Already work on this target
            }
            sc::status_change_start(c, Some(src), target, type_, 10000, skill_lv as i32, src.id as i32, 0, 0, skill_get_time2(id, skill_lv) as i64, SCSTART_NOTICKDEF, skill_get_time(id, skill_lv));
        } else {
            // ( sd? sd->status.job_level : 50 ) / 4
            let job_level = if src.kind == BL_PC { crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32).unwrap_or(0) } else { 50 };
            let rate = 45 + 5 * skill_lv as i32 + job_level / 4;
            // IroWiki says Rate should be reduced by target stats, but currently unknown
            if ((c.rnd() % 100) as i32) < rate {
                // Success on First Target
                if sc::status_change_start(c, Some(src), target, type_, 10000, skill_lv as i32, src.id as i32, 0, 0, skill_get_time2(id, skill_lv) as i64, SCSTART_NOTICKDEF, skill_get_time(id, skill_lv)) {
                    let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                    clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
                    area::set_temp(1, target.id as i32);
                    let f = *flag | BCT_ENEMY | 1;
                    foreach_in_range(c, &t, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| {
                        area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
                    });
                }
                // Doesn't send failure packet if it fails on defense.
            } else if src.kind == BL_PC {
                // Failure on Rate
                skill_fail(c, src, id);
            }
        }
    }
}
