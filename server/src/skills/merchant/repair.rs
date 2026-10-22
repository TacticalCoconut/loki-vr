//! skills/merchant/repair.cpp: class SkillRepair : public SkillImpl (NC_REPAIR).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::fail::USESKILL_FAIL_TOTARGET;
use crate::skill::clif::{clif_skill_nodamage, skill_fail_cause};
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillRepair::SkillRepair() : SkillImpl(NC_REPAIR).
pub struct SkillRepair;

impl SkillImpl for SkillRepair {
    fn skill_id(&self) -> u16 {
        NC_REPAIR
    }

    /// SkillRepair::castendNoDamageId: a player repairs a player on a Mado Gear (else USESKILL_FAIL_TOTARGET): 4 / 7 /
    /// 13 / 17 / 23% of its max HP (status_heal flag 2), the skill packet succeeding when that is not 0.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        // !dstsd || !pc_ismadogear(dstsd)
        if target.kind != BL_PC || target.sc.option & crate::status::defs::OPTION_MADOGEAR == 0 {
            skill_fail_cause(c, src, id, USESKILL_FAIL_TOTARGET);
            return;
        }
        let hp: u32 = match skill_lv {
            1 => 4,
            2 => 7,
            3 => 13,
            4 => 17,
            _ => 23,
        };
        // int32 heal = dstsd->status.max_hp * hp / 100 (uint32 arithmetic)
        let max_hp = crate::pc::character_of(c, target.id).map(|ch| ch.max_hp).unwrap_or(0);
        let heal = (max_hp.wrapping_mul(hp) / 100) as i32;
        crate::status::hp::status_heal(c, target.id, heal as i64, 0, 0, 2);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, heal != 0);
    }
}
