//! skills/other/equipswitch.cpp: class SkillEquipSwitch : public SkillImpl (ALL_EQSWITCH).

use crate::db::skills::*;
use crate::inventory::{EQI_MAX, EQUIP_BITMASK};
use crate::map::BL_PC;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEquipSwitch;

impl SkillImpl for SkillEquipSwitch {
    fn skill_id(&self) -> u16 {
        ALL_EQSWITCH
    }

    /// SkillEquipSwitch::castendNoDamageId: the switch reply (success), then every item of the switch window whose
    /// slots were not switched yet is swapped with the worn ones (pc_equipswitch; sd->equip_switch_index is read
    /// again at each slot, as pc_equipswitch changes it).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        crate::clif_items::equipswitch_reply(c, src.id, false);
        let mut position: u32 = 0;
        for i in 0..EQI_MAX {
            let index = crate::inventory::st(c, src.id).map(|s| s.equip_switch_index[i]).unwrap_or(-1);
            if index >= 0 && position & EQUIP_BITMASK[i] == 0 {
                position |= crate::pc_equip::pc_equipswitch(c, src.id, index as usize);
            }
        }
    }
}
