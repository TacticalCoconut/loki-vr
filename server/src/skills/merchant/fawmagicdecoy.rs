//! skills/merchant/fawmagicdecoy.cpp: class SkillFawMagicDecoy : public SkillImpl (NC_MAGICDECOY).
//!
//! Also the two functions only this skill reaches: clif.cpp clif_magicdecoy_list (the element-stone list, answered with
//! CZ_REQ_MAKINGARROW: itemui/produce.rs clif_parse_SelectArrow calls [`skill_magicdecoy`]) and skill.cpp
//! skill_magicdecoy (the decoy of the chosen stone).

use super::ids::*;
use crate::clif::{self, Target};
use crate::db::items::group::itemdb_group_item_exists;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::net::packets::*;
use crate::skill::clif::skill_fail;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// itemdb.hpp IG_ELEMENT (script constant).
pub const IG_ELEMENT: u16 = 473;
/// itemdb.hpp ITEMID_SCARLET_PTS / ITEMID_INDIGO_PTS / ITEMID_YELLOW_WISH_PTS / ITEMID_LIME_GREEN_PTS.
pub const ITEMID_SCARLET_PTS: u32 = 6360;
pub const ITEMID_INDIGO_PTS: u32 = 6361;
pub const ITEMID_YELLOW_WISH_PTS: u32 = 6362;
pub const ITEMID_LIME_GREEN_PTS: u32 = 6363;
/// mob.hpp MOBID_MAGICDECOY_WATER / MOBID_MAGICDECOY_EARTH (FIRE and WIND are in super::ids).
pub const MOBID_MAGICDECOY_WATER: i32 = 2044;
pub const MOBID_MAGICDECOY_EARTH: i32 = 2045;

/// SkillFawMagicDecoy::SkillFawMagicDecoy() : SkillImpl(NC_MAGICDECOY).
pub struct SkillFawMagicDecoy;

impl SkillImpl for SkillFawMagicDecoy {
    fn skill_id(&self) -> u16 {
        NC_MAGICDECOY
    }

    /// SkillFawMagicDecoy::castendPos2: a player gets the list of its element stones (clif_magicdecoy_list).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind == BL_PC {
            clif_magicdecoy_list(c, src, skill_lv, x, y);
        }
    }
}

/// clif.cpp clif_magicdecoy_list: ZC_MAKINGARROW_LIST with every inventory item of the IG_ELEMENT group (in inventory
/// order, one entry per stack); with at least one, the menu skill (NC_MAGICDECOY, the level) and the cell
/// (sd.sc.comet_x / comet_y) are remembered, else clif_skill_fail.
pub fn clif_magicdecoy_list(c: &Ctx, sd: &Unit, skill_lv: u16, x: i16, y: i16) {
    let aid = sd.id;
    let mut p = PACKET_ZC_MAKINGARROW_LIST { packetType: PACKET_ZC_MAKINGARROW_LIST::ID as i16, ..Default::default() };
    for r in crate::inventory::inv_array(c, aid).into_iter().flatten() {
        if itemdb_group_item_exists(IG_ELEMENT, r.item.nameid) {
            p.items.push(PACKET_ZC_MAKINGARROW_LIST_sub { itemId: crate::db::items::client_nameid(r.item.nameid) });
        }
    }
    if !p.items.is_empty() {
        clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
        let (_, _, val2) = crate::itemui::menu(c, aid);
        crate::itemui::set_menu(c, aid, NC_MAGICDECOY as i32, skill_lv as i32, val2);
        let mut us = crate::status::sc::unit_sc(c, aid);
        us.comet_x = x;
        us.comet_y = y;
        crate::status::sc::put_unit_sc(c, &us);
    } else {
        skill_fail(c, sd, NC_MAGICDECOY);
    }
}

/// skill.cpp skill_magicdecoy: the chosen element stone becomes a Magic Decoy of its element at the remembered cell,
/// attached to the player (AI_FAW, ud->skill_id / skill_lv = NC_MAGICDECOY / the level) for skill_get_time.
///
/// `pc_delitem(&sd, i, 1, 0, 0, LOG_TYPE_CONSUME)`: type 0, reason 0.
/// rAthena's quirk kept: the stone is deleted once inside the validity test (`pc_delitem(...)` answers 0 when it
/// worked) and once more after it, so a player with two stones loses both (with one, the second pc_delitem fails
/// quietly).
pub fn skill_magicdecoy(c: &Ctx, aid: u32, nameid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let skill = crate::itemui::menu(c, aid).1;
    let mut i = -1i16;
    let invalid = nameid == 0 || !itemdb_group_item_exists(IG_ELEMENT, nameid) || {
        i = crate::inventory::pc_search_inventory(c, aid, nameid);
        i < 0
    } || skill == 0
        || crate::inventory::pc_delitem(c, aid, i as i32, 1, 0, 0, crate::map_log::LOG_TYPE_CONSUME) != 0;
    if invalid {
        skill_fail(c, &sd, NC_MAGICDECOY);
        return;
    }
    // Spawn Position
    crate::inventory::pc_delitem(c, aid, i as i32, 1, 0, 0, crate::map_log::LOG_TYPE_CONSUME);
    let mut us = crate::status::sc::unit_sc(c, aid);
    let (x, y) = (us.comet_x, us.comet_y);
    us.comet_x = 0;
    us.comet_y = 0;
    crate::status::sc::put_unit_sc(c, &us);
    let (mid, _, val2) = crate::itemui::menu(c, aid);
    crate::itemui::set_menu(c, aid, mid, 0, val2);
    // Item picked decides the mob class
    let class_ = match nameid {
        ITEMID_SCARLET_PTS => MOBID_MAGICDECOY_FIRE,
        ITEMID_INDIGO_PTS => MOBID_MAGICDECOY_WATER,
        ITEMID_YELLOW_WISH_PTS => MOBID_MAGICDECOY_WIND,
        ITEMID_LIME_GREEN_PTS => MOBID_MAGICDECOY_EARTH,
        _ => {
            if let Some(sd) = unit::get(c, aid) {
                skill_fail(c, &sd, NC_MAGICDECOY);
            }
            return;
        }
    };
    let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    let sd = unit::get(c, aid).unwrap_or(sd);
    let Some(md) = crate::mob::spawn::mob_once_spawn_sub(c, Some(&sd), sd.map, x, y, Some(&name), class_, Some(""), crate::status::defs::SZ_SMALL as u8, crate::mob::AI_NONE) else {
        return;
    };
    if let Some(mut u) = unit::get(c, md.id) {
        u.ud.skill_id = NC_MAGICDECOY;
        u.ud.skill_lv = skill as u16;
        unit::save(c, &u);
    }
    super::summon_attach_and_spawn(c, md, Some(aid), crate::mob::AI_FAW as i32, c.tick() + skill_get_time(NC_MAGICDECOY, skill as u16) as Tick);
}

#[cfg(test)]
mod tests {
    #[test]
    fn constants_match_the_script_constants() {
        let k = |n: &str| crate::data::constant(n);
        assert_eq!(k("IG_ELEMENT"), Some(super::IG_ELEMENT as i64));
        let item = |id: u32| crate::db::items::itemdb_exists(id).map(|d| d.name.clone()).unwrap_or_default();
        assert_eq!(item(super::ITEMID_SCARLET_PTS), "Scarlet_Pts");
        assert_eq!(item(super::ITEMID_INDIGO_PTS), "Indigo_Pts");
        assert_eq!(item(super::ITEMID_YELLOW_WISH_PTS), "Yellow_Wish_Pts");
        assert_eq!(item(super::ITEMID_LIME_GREEN_PTS), "Lime_Green_Pts");
        let mob = |id: i32| crate::db::mobs::mob_db().find(id as u32).map(|m| m.sprite.clone()).unwrap_or_default();
        assert_eq!(mob(super::MOBID_MAGICDECOY_WATER), "MAGICDECOY_WATER");
        assert_eq!(mob(super::MOBID_MAGICDECOY_EARTH), "MAGICDECOY_EARTH");
    }
}
