//! The item windows of rAthena e985006 (PACKETVER 20211103, RENEWAL): the refine UI, weapon refine / repair /
//! identify lists, card insertion, enchant grade, the enchant window, item reform, Laphine synthesis / upgrade, the
//! stylist, item merge, and crafting (produce / cooking / arrows / runes); their @commands and script commands.
//!
//! This file holds the per-player state those windows keep in map_session_data (pc.hpp):
//! - `sd->menuskill_id` / `menuskill_val` / `menuskill_val2`: the skill-driven selection list that is open
//!   (identify, weapon refine, repair, produce, cooking, arrow crafting; clif.hpp clif_menuskill_clear);
//! - `sd->state.workinprogress` (clif.hpp e_workinprogress, pc.hpp pc_hasprogress);
//! - `sd->state.refineui_open`, `stylist_open`, `enchantgrade_open`, `laphine_synthesis`, `laphine_upgrade`,
//!   `item_reform`, `item_enchant_index` — the windows pc_cant_act2 tests. They are mirrored into the `pc.ui_open`
//!   bits below, which is what the module's pc_cant_act / pc_cant_act2 read;
//! - `sd->potion_success_counter` (the fame streak of skill_produce_mix).
//!
//! pc_authok creates map_session_data zeroed ([`on_authok`]); pc_setpos clears the window flags and workinprogress
//! ([`on_setpos`]); unit_free drops the row ([`on_unit_free`]).

pub mod atcommand;
pub mod buildins;
pub mod card;
pub mod enchant;
pub mod grade;
pub mod identify;
pub mod itemdb_rt;
pub mod laphine;
pub mod look;
pub mod merge;
pub mod produce;
pub mod refine;
pub mod reform;
pub mod repair;
pub mod stylist;

#[cfg(test)]
mod tests;

use spacetimedb::{table, Table};

use crate::world::Ctx;

// ---------------------------------------------------------------------------------------------- pc.ui_open bits

/// `pc.ui_open` bit of sd->state.refineui_open.
pub const UI_REFINE: u32 = 1 << 0;
/// sd->state.stylist_open.
pub const UI_STYLIST: u32 = 1 << 1;
/// sd->state.laphine_synthesis != 0.
pub const UI_LAPHINE_SYNTHESIS: u32 = 1 << 2;
/// sd->state.laphine_upgrade != 0.
pub const UI_LAPHINE_UPGRADE: u32 = 1 << 3;
/// sd->state.enchantgrade_open.
pub const UI_ENCHANTGRADE: u32 = 1 << 4;
/// sd->state.item_reform != 0.
pub const UI_REFORM: u32 = 1 << 5;
/// sd->state.item_enchant_index != 0.
pub const UI_ENCHANT: u32 = 1 << 6;
/// Every bit this system owns (other systems' windows — inventory expansion, roulette — use higher bits).
pub const UI_ITEMUI_MASK: u32 = 0x7f;

// ---------------------------------------------------------------------------------------------- clif.hpp e_workinprogress

pub const WIP_DISABLE_NONE: u8 = 0x0;
pub const WIP_DISABLE_SKILLITEM: u8 = 0x1;
pub const WIP_DISABLE_NPC: u8 = 0x2;
pub const WIP_DISABLE_ALL: u8 = 0x3;

// ---------------------------------------------------------------------------------------------- battle_config

/// conf/battle/feature.conf `feature.refineui: on`.
pub fn feature_refineui() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_REFINEUI) != 0
}
/// conf/battle/feature.conf `feature.stylist: on`.
pub fn feature_stylist() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_STYLIST) != 0
}
/// conf/battle/items.conf `broadcast_hide_name: 2`.
pub fn broadcast_hide_name() -> usize {
    crate::battle_config::get(crate::battle_config::Bc::BROADCAST_HIDE_NAME) as usize
}
/// conf/battle/items.conf `produce_item_name_input: 0x03`.
pub fn produce_item_name_input() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PRODUCE_ITEM_NAME_INPUT)
}
/// conf/battle/items.conf `weapon_produce_rate: 100`: battle_config.wp_rate (battle.cpp:8325, 0 to INT_MAX), read at
/// run time (battle_config/mod.rs).
pub fn wp_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::WEAPON_PRODUCE_RATE)
}
/// conf/battle/items.conf `potion_produce_rate: 100`: battle_config.pp_rate (battle.cpp:8326, 0 to INT_MAX), read at
/// run time.
pub fn pp_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::POTION_PRODUCE_RATE)
}
/// conf/battle/skill.conf `skill_drop_items_full: no`.
pub fn skill_drop_items_full() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_DROP_ITEMS_FULL) != 0
}
/// conf/battle/skill.conf `oridecon_research_fix: no`.
pub fn oridecon_research_fix() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::ORIDECON_RESEARCH_FIX)
}
/// conf/battle/player.conf fame_refine_lv1 / lv2 / lv3, fame_forge, fame_pharmacy_3 / 5 / 7 / 10.
pub fn fame_refine_lv1() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_REFINE_LV1)
}
pub fn fame_refine_lv2() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_REFINE_LV2)
}
pub fn fame_refine_lv3() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_REFINE_LV3)
}
pub fn fame_forge() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_FORGE)
}
pub fn fame_pharmacy_3() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_PHARMACY_3)
}
pub fn fame_pharmacy_5() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_PHARMACY_5)
}
pub fn fame_pharmacy_7() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_PHARMACY_7)
}
pub fn fame_pharmacy_10() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_PHARMACY_10)
}

/// clif.cpp color_table[COLOR_RED]: do_init_clif stores the colors converted from RGB to BGR (0xFF0000 -> 0x0000FF).
pub const COLOR_RED: u32 = 0x0000ff;

// ---------------------------------------------------------------------------------------------- state

/// map_session_data's item-window state (one row per online character, key = account id).
#[table(accessor = pc_itemui)]
#[derive(Clone, Debug, Default)]
pub struct PcItemui {
    #[primary_key]
    pub account_id: u32,
    /// sd->menuskill_id (a skill id, -1 for the `produce` script list, 0 = none).
    pub menuskill_id: i32,
    pub menuskill_val: i32,
    pub menuskill_val2: i32,
    /// sd->state.workinprogress (WIP_*).
    pub workinprogress: u8,
    pub refineui_open: bool,
    pub stylist_open: bool,
    pub enchantgrade_open: bool,
    /// sd->state.laphine_synthesis / laphine_upgrade / item_reform: the item that opened the window (0 = closed).
    pub laphine_synthesis: u32,
    pub laphine_upgrade: u32,
    pub item_reform: u32,
    /// sd->state.item_enchant_index: the enchant group (clientLuaIndex) of the open enchant window (0 = closed).
    pub item_enchant_index: u64,
    /// sd->potion_success_counter.
    pub potion_success_counter: i32,
}

/// The player's item-window state (a zeroed map_session_data part when there is no row yet).
pub fn state(c: &Ctx, aid: u32) -> PcItemui {
    c.db().pc_itemui().account_id().find(aid).unwrap_or(PcItemui { account_id: aid, ..Default::default() })
}

/// Writes the state back and mirrors the window flags into `pc.ui_open` (pc_cant_act / pc_cant_act2).
pub fn put_state(c: &Ctx, s: &PcItemui) {
    if c.db().pc_itemui().account_id().find(s.account_id).is_some() {
        c.db().pc_itemui().account_id().update(s.clone());
    } else {
        c.db().pc_itemui().insert(s.clone());
    }
    let bits = ui_bits(s);
    if let Some(mut p) = crate::pc::get(c, s.account_id) {
        let v = (p.ui_open & !UI_ITEMUI_MASK) | bits;
        if v != p.ui_open {
            p.ui_open = v;
            crate::pc::put(c, &p);
        }
    }
}

fn ui_bits(s: &PcItemui) -> u32 {
    let mut b = 0;
    if s.refineui_open {
        b |= UI_REFINE;
    }
    if s.stylist_open {
        b |= UI_STYLIST;
    }
    if s.laphine_synthesis != 0 {
        b |= UI_LAPHINE_SYNTHESIS;
    }
    if s.laphine_upgrade != 0 {
        b |= UI_LAPHINE_UPGRADE;
    }
    if s.enchantgrade_open {
        b |= UI_ENCHANTGRADE;
    }
    if s.item_reform != 0 {
        b |= UI_REFORM;
    }
    if s.item_enchant_index != 0 {
        b |= UI_ENCHANT;
    }
    b
}

/// Changes the state with `f` and writes it back.
pub fn update(c: &Ctx, aid: u32, f: impl FnOnce(&mut PcItemui)) {
    let mut s = state(c, aid);
    f(&mut s);
    put_state(c, &s);
}

/// pc_authok: map_session_data starts zeroed (menuskill, workinprogress, the window flags, the potion counter).
pub fn on_authok(c: &Ctx, aid: u32) {
    c.db().pc_itemui().account_id().delete(aid);
}

/// pc.cpp pc_setpos: `sd->state.workinprogress = WIP_DISABLE_NONE`, and refineui_open, stylist_open, laphine_synthesis,
/// laphine_upgrade, enchantgrade_open, item_reform, item_enchant_index are reset (the caller clears `pc.ui_open`).
/// menuskill_* and potion_success_counter survive a warp.
pub fn on_setpos(c: &Ctx, aid: u32) {
    let Some(mut s) = c.db().pc_itemui().account_id().find(aid) else { return };
    s.workinprogress = WIP_DISABLE_NONE;
    s.refineui_open = false;
    s.stylist_open = false;
    s.laphine_synthesis = 0;
    s.laphine_upgrade = 0;
    s.enchantgrade_open = false;
    s.item_reform = 0;
    s.item_enchant_index = 0;
    c.db().pc_itemui().account_id().update(s);
}

/// unit_free: the session state goes.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_itemui().account_id().delete(aid);
}

/// pc.hpp pc_hasprogress: `(sd->state.workinprogress & progress) == progress`.
pub fn pc_hasprogress(c: &Ctx, aid: u32, progress: u8) -> bool {
    state(c, aid).workinprogress & progress == progress
}

/// sd->state.workinprogress = `v`.
pub fn set_workinprogress(c: &Ctx, aid: u32, v: u8) {
    update(c, aid, |s| s.workinprogress = v);
}

/// (sd->menuskill_id, menuskill_val, menuskill_val2).
pub fn menu(c: &Ctx, aid: u32) -> (i32, i32, i32) {
    let s = state(c, aid);
    (s.menuskill_id, s.menuskill_val, s.menuskill_val2)
}

/// Sets sd->menuskill_id / menuskill_val / menuskill_val2.
pub fn set_menu(c: &Ctx, aid: u32, id: i32, val: i32, val2: i32) {
    update(c, aid, |s| {
        s.menuskill_id = id;
        s.menuskill_val = val;
        s.menuskill_val2 = val2;
    });
}

/// clif.hpp clif_menuskill_clear: `menuskill_id = menuskill_val = menuskill_val2 = 0`.
pub fn clif_menuskill_clear(c: &Ctx, aid: u32) {
    if c.db().pc_itemui().account_id().find(aid).is_some() {
        set_menu(c, aid, 0, 0, 0);
    }
}

// ---------------------------------------------------------------------------------------------- shared helpers

/// clif.cpp server_index: a client inventory index minus 2, as uint16 (0 and 1 wrap to 65534 / 65535).
pub fn server_index(client_index: u16) -> u16 {
    client_index.wrapping_sub(2)
}

/// The inventory slot `idx` (None: empty slot, i.e. `inventory_data[idx] == nullptr`).
pub fn slot(c: &Ctx, aid: u32, idx: usize) -> Option<crate::inventory::InventoryItem> {
    crate::inventory::inv_get(c, aid, idx)
}

/// clif.cpp clif_misceffect on the player (ZC_NOTIFYEFFECT… to the area).
pub fn misceffect(c: &Ctx, aid: u32, kind: i32) {
    if let Some(sd) = crate::unit::get(c, aid) {
        crate::status::clif::misceffect(c, &sd, kind);
    }
}

/// clif.cpp clif_hide_name: the last min(broadcast_hide_name, len - 1) characters become '*'.
pub fn clif_hide_name(name: &str) -> String {
    let chars: Vec<char> = name.chars().collect();
    if chars.is_empty() {
        // censored.length() - 1 underflows size_t: zmin keeps broadcast_hide_name, and replace() on an empty string
        // with a count past the end throws; rAthena never calls it with an empty name
        return String::new();
    }
    let hide = broadcast_hide_name().min(chars.len() - 1);
    let keep = chars.len() - hide;
    let mut s: String = chars[..keep].iter().collect();
    for _ in 0..hide {
        s.push('*');
    }
    s
}

/// The iteration order of a `std::unordered_map<uint16, …>` / `std::unordered_set<uint16>` filled with `keys` in this
/// order (libstdc++: see db::jobs::libstdcxx_unordered_order). rAthena builds such maps of inventory indexes and then
/// deletes the items in the map's order.
pub fn umap_order(keys: &[u16]) -> Vec<u16> {
    let k: Vec<u64> = keys.iter().map(|&k| k as u64).collect();
    crate::db::jobs::libstdcxx_unordered_order(&k).into_iter().map(|k| k as u16).collect()
}

/// pc.cpp pc_addfame for a refine, forge or potion streak: the extras agent's fame.rs.
pub fn pc_addfame_stub(c: &Ctx, aid: u32, count: i32) {
    crate::fame::pc_addfame(c, aid, count);
}

/// clif.hpp enum out_ui_type (the ones these windows use).
pub const OUT_UI_STYLIST: i8 = 1;
pub const OUT_UI_ENCHANTGRADE: i8 = 8;
pub const OUT_UI_ENCHANT: u8 = 10;

/// clif.cpp clif_ui_open for the windows that need state tracking here (stylist_open / enchantgrade_open are set
/// first), then ZC_UI_OPEN. (The attendance and quest UIs are opened by their own systems.)
pub fn clif_ui_open(c: &Ctx, aid: u32, ui_type: i8, data: i32) {
    match ui_type {
        OUT_UI_STYLIST => update(c, aid, |s| s.stylist_open = true),
        OUT_UI_ENCHANTGRADE => update(c, aid, |s| s.enchantgrade_open = true),
        _ => {}
    }
    let p = crate::net::packets::PACKET_ZC_UI_OPEN { PacketType: crate::net::packets::PACKET_ZC_UI_OPEN::ID as i16, UIType: ui_type, data };
    if let Some(sd) = crate::unit::get(c, aid) {
        crate::clif::send(c, &p.to_vec(), &sd, crate::clif::Target::SelfOnly);
    }
}

// ---------------------------------------------------------------------------------------------- packet dispatch

/// The clif_parse_* functions of these windows, by the parser name of the packet table (net/mod.rs dispatch);
/// false when `func` is not one of them.
pub fn parse(c: &Ctx, aid: u32, func: &str, pkt: &[u8]) -> bool {
    match func {
        "clif_parse_ItemIdentify" => identify::clif_parse_item_identify(c, aid, pkt),
        "clif_parse_Oneclick_Itemidentify" => identify::clif_parse_oneclick_itemidentify(c, aid, pkt),
        "clif_parse_UseCard" => card::clif_parse_use_card(c, aid, pkt),
        "clif_parse_InsertCard" => card::clif_parse_insert_card(c, aid, pkt),
        "clif_parse_refineui_add" => refine::clif_parse_refineui_add(c, aid, pkt),
        "clif_parse_refineui_refine" => refine::clif_parse_refineui_refine(c, aid, pkt),
        "clif_parse_refineui_close" => refine::clif_parse_refineui_close(c, aid),
        "clif_parse_WeaponRefine" => refine::clif_parse_weapon_refine(c, aid, pkt),
        "clif_parse_RepairItem" => repair::clif_parse_repair_item(c, aid, pkt),
        "clif_parse_enchantgrade_add" => grade::clif_parse_enchantgrade_add(c, aid, pkt),
        "clif_parse_enchantgrade_start" => grade::clif_parse_enchantgrade_start(c, aid, pkt),
        "clif_parse_enchantgrade_close" => grade::clif_parse_enchantgrade_close(c, aid),
        "clif_parse_enchantwindow_general" => enchant::clif_parse_enchantwindow_general(c, aid, pkt),
        "clif_parse_enchantwindow_perfect" => enchant::clif_parse_enchantwindow_perfect(c, aid, pkt),
        "clif_parse_enchantwindow_upgrade" => enchant::clif_parse_enchantwindow_upgrade(c, aid, pkt),
        "clif_parse_enchantwindow_reset" => enchant::clif_parse_enchantwindow_reset(c, aid, pkt),
        "clif_parse_enchantwindow_close" => enchant::clif_parse_enchantwindow_close(c, aid),
        "clif_parse_item_reform_start" => reform::clif_parse_item_reform_start(c, aid, pkt),
        "clif_parse_item_reform_close" => reform::clif_parse_item_reform_close(c, aid),
        "clif_parse_laphine_synthesis" => laphine::clif_parse_laphine_synthesis(c, aid, pkt),
        "clif_parse_laphine_synthesis_close" => laphine::clif_parse_laphine_synthesis_close(c, aid),
        "clif_parse_laphine_upgrade" => laphine::clif_parse_laphine_upgrade(c, aid, pkt),
        "clif_parse_laphine_upgrade_close" => laphine::clif_parse_laphine_upgrade_close(c, aid),
        "clif_parse_stylist_buy" => stylist::clif_parse_stylist_buy(c, aid, pkt),
        "clif_parse_stylist_close" => stylist::clif_parse_stylist_close(c, aid),
        "clif_parse_merge_item_req" => merge::clif_parse_merge_item_req(c, aid, pkt),
        "clif_parse_merge_item_cancel" => merge::clif_parse_merge_item_cancel(c, aid),
        "clif_parse_ProduceMix" => produce::clif_parse_produce_mix(c, aid, pkt),
        "clif_parse_Cooking" => produce::clif_parse_cooking(c, aid, pkt),
        "clif_parse_SelectArrow" => produce::clif_parse_select_arrow(c, aid, pkt),
        _ => return false,
    }
    true
}
