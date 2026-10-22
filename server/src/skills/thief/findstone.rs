//! skills/thief/findstone.cpp: class SkillFindStone : public SkillImpl (TF_PICKSTONE).

use crate::db::skills::*;
use crate::inventory::Item;
use crate::map::{BL_CHAR, BL_PC, BL_PET};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// itemdb.hpp ITEMID_STONE.
pub const ITEMID_STONE: u32 = 7049;
/// conf/battle/skill.conf `skill_drop_items_full: no` (no Loki override).
pub fn skill_drop_items_full() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_DROP_ITEMS_FULL)
}

pub struct SkillFindStone;

impl SkillImpl for SkillFindStone {
    fn skill_id(&self) -> u16 {
        TF_PICKSTONE
    }

    /// SkillFindStone::castendNoDamageId: the animation, then one identified Stone into the inventory (the failure
    /// packet when it does not fit; no floor drop with skill_drop_items_full off). The duplicate pickup animation
    /// (clif_takeitem) is commented out in rAthena [Lemongrass].
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        clif_skill_nodamage(c, Some(src), bl, self.skill_id(), skill_lv as i32, true);
        let mut item_tmp = Item { nameid: ITEMID_STONE, identify: 1, ..Default::default() };
        // clif_takeitem(*sd,tbl): commented out in rAthena
        let eflag = crate::inventory::pc_additem(c, src.id, &mut item_tmp, 1, crate::map_log::LOG_TYPE_PRODUCE);
        if eflag != 0 {
            crate::clif_items::additem(c, src.id, 0, 0, eflag);
            if skill_drop_items_full() != 0 {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                crate::flooritem::map_addflooritem(c, &item_tmp, 1, s.map, s.x, s.y, 0, 0, 0, 4, 0, false, None, BL_CHAR | BL_PET);
            }
        }
    }
}
