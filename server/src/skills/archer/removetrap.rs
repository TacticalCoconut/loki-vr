//! skills/archer/removetrap.cpp: class SkillRemoveTrap : public SkillImpl (HT_REMOVETRAP).

use crate::db::skills::*;
use crate::inventory::Item;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::skillunit::consts::{UNT_ANKLESNARE, UNT_USED_TRAPS};
use crate::skillunit::data;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// itemdb.hpp ITEMID_TRAP.
pub const ITEMID_TRAP: u32 = 1065;

pub struct SkillRemoveTrap;

impl SkillImpl for SkillRemoveTrap {
    fn skill_id(&self) -> u16 {
        HT_REMOVETRAP
    }

    /// SkillRemoveTrap::castendNoDamageId: a player removes one of its own traps (or any trap on a PvP / GvG map):
    /// the no-damage packet, the trap's item back unless it is spent (a used trap, an Ankle Snare holding someone),
    /// then the unit goes; anything else fails.
    ///
    /// rAthena's "get back 1 trap" branch stores pc_additem's answer in the method's `int32& flag` parameter
    /// (`flag=pc_additem(…)`), so skill_castend_nodamage_id continues with that value as its flag: kept here.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != crate::map::BL_PC {
            return;
        }
        // skill_unit* su = BL_CAST(BL_SKILL, target); sg = su->group
        let su = if target.kind == crate::map::BL_SKILL { data::get_unit(c, target.id) } else { None };
        let sg = su.as_ref().and_then(|su| data::skill_id2group(c, su.group_id));
        let trap = match (&su, &sg) {
            // Players can only remove their own traps or traps on Vs maps.
            (Some(_), Some(sg)) => (sg.src_id == src.id || crate::npc::mapflag_vs(c, target.map)) && skill_db().find(sg.skill_id).map(|s| s.has_inf2(INF2_ISTRAP)).unwrap_or(false),
            _ => false,
        };
        let (Some(su), Some(sg), true) = (su, sg, trap) else {
            skill_fail(c, src, id);
            return;
        };
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        if !(sg.unit_id == UNT_USED_TRAPS || (sg.unit_id == UNT_ANKLESNARE && sg.val2 != 0)) {
            // prevent picking up expired traps
            if super::skill_removetrap_type() {
                // get back all items used to deploy the trap
                if let Some(skill_group) = skill_db().find(sg.skill_id) {
                    for i in 0..MAX_SKILL_ITEM_REQUIRE {
                        if skill_group.require.itemid[i] > 0 {
                            let mut item_tmp = Item::new(skill_group.require.itemid[i]);
                            item_tmp.identify = 1;
                            item_tmp.amount = skill_group.require.amount[i] as i16;
                            if item_tmp.nameid != 0 {
                                let amount = item_tmp.amount as i32;
                                let flag2 = crate::inventory::pc_additem(c, src.id, &mut item_tmp, amount, crate::map_log::LOG_TYPE_OTHER);
                                if flag2 != 0 {
                                    crate::clif_items::additem(c, src.id, 0, 0, flag2);
                                    if crate::itemui::skill_drop_items_full() {
                                        crate::flooritem::map_addflooritem(c, &item_tmp, amount, src.map, src.x, src.y, 0, 0, 0, 4, 0, false, None, 0);
                                    }
                                }
                            }
                        }
                    }
                }
            } else {
                // get back 1 trap
                let mut item_tmp = Item::new(if sg.item_id != 0 { sg.item_id } else { ITEMID_TRAP });
                item_tmp.identify = 1;
                if item_tmp.nameid != 0 {
                    *flag = crate::inventory::pc_additem(c, src.id, &mut item_tmp, 1, crate::map_log::LOG_TYPE_OTHER) as i32;
                    if *flag != 0 {
                        crate::clif_items::additem(c, src.id, 0, 0, *flag as u8);
                        if crate::itemui::skill_drop_items_full() {
                            crate::flooritem::map_addflooritem(c, &item_tmp, 1, src.map, src.x, src.y, 0, 0, 0, 4, 0, false, None, 0);
                        }
                    }
                }
            }
        }
        crate::skill::iface::skill_delunit(c, su.id());
    }
}
