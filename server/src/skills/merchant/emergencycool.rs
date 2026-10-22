//! skills/merchant/emergencycool.cpp: class SkillEmergencyCool : public SkillImpl (NC_EMERGENCYCOOL).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillEmergencyCool::SkillEmergencyCool() : SkillImpl(NC_EMERGENCYCOOL).
pub struct SkillEmergencyCool;

impl SkillImpl for SkillEmergencyCool {
    fn skill_id(&self) -> u16 {
        NC_EMERGENCYCOOL
    }

    /// SkillEmergencyCool::castendNoDamageId: the skill packet; a player then cools its Mado Gear by -45 / -75 / -105
    /// heat, by the first of the requirement's equipped items (req.eqItem) it carries (the last one when none).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        if src.kind != BL_PC {
            return;
        }
        let req = crate::skill::condition::skill_get_requirement(c, src, id, skill_lv);
        let limit: [i16; 3] = [-45, -75, -105];
        let mut i = 0usize;
        for req_item in &req.eq_item {
            if crate::inventory::pc_search_inventory(c, src.id, *req_item) != -1 {
                break;
            }
            i += 1;
        }
        crate::battle::fix::pc_overheat(c, src.id, limit[if i > 2 { 2 } else { i }]);
    }
}
