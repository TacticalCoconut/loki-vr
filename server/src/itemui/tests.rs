//! Unit tests of the item windows' pure parts (the packet flows are covered by the parity scenarios in
//! testrig/parity/itemui/).

use super::*;

#[test]
fn hide_name_like_clif_hide_name() {
    // broadcast_hide_name: 2 → the last two characters become '*' (never the whole name)
    assert_eq!(clif_hide_name("Itemui Tester"), "Itemui Test**");
    assert_eq!(clif_hide_name("ab"), "a*");
    assert_eq!(clif_hide_name("a"), "a");
}

#[test]
fn server_index_wraps_like_uint16() {
    assert_eq!(server_index(2), 0);
    assert_eq!(server_index(0), 65534);
    assert_eq!(server_index(1), 65535);
}

#[test]
fn unordered_map_of_two_inventory_indexes_iterates_backwards() {
    // libstdc++: a second key goes in front of the first (whatever their buckets): rAthena deletes the materials of
    // the grade / enchant / reform windows in this order
    assert_eq!(umap_order(&[3, 7]), vec![7, 3]);
    assert_eq!(umap_order(&[7, 3]), vec![3, 7]);
    assert_eq!(umap_order(&[5]), vec![5]);
}

#[test]
fn ui_bits_follow_the_window_flags() {
    let mut s = PcItemui { account_id: 1, ..Default::default() };
    assert_eq!(ui_bits(&s), 0);
    s.refineui_open = true;
    s.item_enchant_index = 4;
    assert_eq!(ui_bits(&s), UI_REFINE | UI_ENCHANT);
    s.laphine_synthesis = 6912;
    assert_eq!(ui_bits(&s) & UI_ITEMUI_MASK, UI_REFINE | UI_ENCHANT | UI_LAPHINE_SYNTHESIS);
}

#[test]
fn window_data_the_scenarios_use() {
    use crate::db::items as itemdb;
    // Blacksmith Blessing, the magnifiers and the weapon refine ores exist
    for id in [refine::ITEMID_BLACKSMITH_BLESSING, identify::ITEMID_MAGNIFIER, identify::ITEMID_NOVICE_MAGNIFIER, refine::ITEMID_PHRACON, refine::ITEMID_ORIDECON] {
        assert!(itemdb::itemdb_exists(id).is_some(), "item {id}");
    }
    // a +0 Knife (1201, weapon level 1) refines with Phracon in the normal cost of the refine database
    let knife = itemdb::itemdb_search(1201);
    let info = itemdb::refine::find_level_info(knife, 0).expect("refine info of a +0 knife");
    assert_eq!(info.cost(itemdb::refine::REFINE_COST_NORMAL).map(|c| c.nameid), Some(refine::ITEMID_PHRACON));
    // every laphine box, reform ticket and enchant group used by an item script is in its database
    assert!(!itemdb::laphine::laphine_synthesis_db().is_empty());
    assert!(!itemdb::laphine::laphine_upgrade_db().is_empty());
    assert!(!itemdb::reform::item_reform_db().is_empty());
    assert!(!itemdb::enchant::item_enchant_db().is_empty());
}

#[test]
fn unordered_map_of_three_indexes() {
    // the perfect enchant of the enchant scenario deletes its three materials at indexes 5, 4, 2 (oracle order)
    assert_eq!(umap_order(&[2, 4, 5]), vec![5, 4, 2]);
}

#[test]
fn setiteminfo_fields() {
    use super::itemdb_rt::*;
    let mut knife = crate::db::items::itemdb_search(1201).clone();
    apply(&mut knife, ITEMINFO_WEIGHT, 7);
    apply(&mut knife, ITEMINFO_BUY, 99);
    apply(&mut knife, ITEMINFO_SLOT, 1);
    // ITEMINFO_VIEW on a weapon sets the subtype ("keep old compatibility")
    apply(&mut knife, ITEMINFO_VIEW, 3);
    assert_eq!((knife.weight, knife.value_buy, knife.slots, knife.subtype), (7, 99, 1, 3));
    let mut shirt = crate::db::items::itemdb_search(2301).clone();
    apply(&mut shirt, ITEMINFO_VIEW, 44);
    assert_eq!(shirt.look, 44);
}
