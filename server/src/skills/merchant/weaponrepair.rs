//! skills/merchant/weaponrepair.cpp: class SkillWeaponRepair : public SkillImpl (BS_REPAIRWEAPON). The list and the
//! repair are itemui's (itemui/repair.rs: clif_item_repair_list, clif_parse_RepairItem, skill_repairweapon).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillWeaponRepair::SkillWeaponRepair() : SkillImpl(BS_REPAIRWEAPON).
pub struct SkillWeaponRepair;

impl SkillImpl for SkillWeaponRepair {
    fn skill_id(&self) -> u16 {
        BS_REPAIRWEAPON
    }

    /// SkillWeaponRepair::castendNoDamageId: a player caster on a player target gets the target's broken items list.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // map_session_data* sd = BL_CAST(BL_PC, src); map_session_data* dstsd = BL_CAST(BL_PC, target)
        if src.kind == BL_PC && target.kind == BL_PC {
            crate::itemui::repair::clif_item_repair_list(c, src.id, target.id, skill_lv);
        }
    }
}
