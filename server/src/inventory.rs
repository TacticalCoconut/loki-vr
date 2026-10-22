//! The items a character carries (pc.cpp: pc_additem, pc_delitem, pc_checkadditem, pc_inventoryblank,
//! pc_search_inventory, pc_payzeny / pc_getzeny, pc_updateweightstatus, pc_isUseitem / pc_useitem, item cooldowns,
//! rentals, pc_checkitem / pc_check_available_item, unique ids) and the inventory part of the character's loading
//! and saving (intif_parse_StorageReceived TABLE_INVENTORY, char_memitemdata_from_sql / _to_sql).
//!
//! Model. rAthena keeps `sd->inventory.u.items_inventory[MAX_INVENTORY]` (index = the server index; the client index
//! is index + 2) and `sd->inventory_data[]` (item_db pointers). Here every occupied slot is one `inventory` row
//! (char_id, idx, item); an empty slot (nameid 0) has no row. `idx` is the rAthena index for the whole session;
//! at login the rows are renumbered in their SQL-id order (char_memitemdata_from_sql: `ORDER BY id`), and
//! `save_id` plays the SQL `id` (assigned by [`inventory_tosql`], char_memitemdata_to_sql's matching rules).
//! `inventory_data[i]` is `itemdb_search(nameid)` (never null for an occupied slot, as pc_setinventorydata makes it).
//! The session state of map_session_data that concerns items (weight, equip_index, cooldowns, combos, …) is the
//! `pc_items` row of the account.

use spacetimedb::{table, SpacetimeType, Table};

use crate::character::{character, Character};
use crate::map_log::*;
use crate::clif::{self, Target};
use crate::db::items::{self as itemdb, ItemData};
use crate::pc;
use crate::unit;
use crate::world::{Ctx, Tick};

// ---------------------------------------------------------------------------------------------- constants

/// mmo.hpp INVENTORY_BASE_SIZE / INVENTORY_EXPANSION_SIZE (PACKETVER >= 20181031) / MAX_INVENTORY.
pub const INVENTORY_BASE_SIZE: i16 = 100;
pub const MAX_INVENTORY: usize = 200;
/// mmo.hpp MAX_SLOTS, MAX_AMOUNT, MAX_ZENY, MAX_ITEM_RDM_OPT, MAX_CART, MAX_STORAGE.
pub const MAX_SLOTS: usize = 4;
pub const MAX_AMOUNT: i32 = 30000;
pub const MAX_ZENY: i64 = i32::MAX as i64;
pub const MAX_ITEM_RDM_OPT: usize = 5;
pub const MAX_CART: usize = 100;
/// itemdb.hpp MAX_ITEMDELAYS.
pub const MAX_ITEMDELAYS: usize = 10;
/// mmo.hpp MAX_REFINE.
pub const MAX_REFINE: i32 = 20;

/// mmo.hpp enum equip_pos.
pub const EQP_HEAD_LOW: u32 = 0x000001;
pub const EQP_HEAD_MID: u32 = 0x000200;
pub const EQP_HEAD_TOP: u32 = 0x000100;
pub const EQP_HAND_R: u32 = 0x000002;
pub const EQP_HAND_L: u32 = 0x000020;
pub const EQP_ARMOR: u32 = 0x000010;
pub const EQP_SHOES: u32 = 0x000040;
pub const EQP_GARMENT: u32 = 0x000004;
pub const EQP_ACC_R: u32 = 0x000008;
pub const EQP_ACC_L: u32 = 0x000080;
pub const EQP_COSTUME_HEAD_TOP: u32 = 0x000400;
pub const EQP_COSTUME_HEAD_MID: u32 = 0x000800;
pub const EQP_COSTUME_HEAD_LOW: u32 = 0x001000;
pub const EQP_COSTUME_GARMENT: u32 = 0x002000;
pub const EQP_AMMO: u32 = 0x008000;
pub const EQP_SHADOW_ARMOR: u32 = 0x010000;
pub const EQP_SHADOW_WEAPON: u32 = 0x020000;
pub const EQP_SHADOW_SHIELD: u32 = 0x040000;
pub const EQP_SHADOW_SHOES: u32 = 0x080000;
pub const EQP_SHADOW_ACC_R: u32 = 0x100000;
pub const EQP_SHADOW_ACC_L: u32 = 0x200000;
/// pc.hpp combined positions.
pub const EQP_WEAPON: u32 = EQP_HAND_R;
pub const EQP_SHIELD: u32 = EQP_HAND_L;
pub const EQP_ARMS: u32 = EQP_HAND_R | EQP_HAND_L;
pub const EQP_HELM: u32 = EQP_HEAD_LOW | EQP_HEAD_MID | EQP_HEAD_TOP;
pub const EQP_ACC: u32 = EQP_ACC_L | EQP_ACC_R;
pub const EQP_COSTUME: u32 = EQP_COSTUME_HEAD_TOP | EQP_COSTUME_HEAD_MID | EQP_COSTUME_HEAD_LOW | EQP_COSTUME_GARMENT;
pub const EQP_SHADOW_GEAR: u32 = EQP_SHADOW_ARMOR | EQP_SHADOW_WEAPON | EQP_SHADOW_SHIELD | EQP_SHADOW_SHOES | EQP_SHADOW_ACC_R | EQP_SHADOW_ACC_L;
pub const EQP_SHADOW_ACC: u32 = EQP_SHADOW_ACC_R | EQP_SHADOW_ACC_L;
pub const EQP_SHADOW_ARMS: u32 = EQP_SHADOW_WEAPON | EQP_SHADOW_SHIELD;
/// pc.hpp EQP_VISIBLE (PACKETVER >= 20110111).
pub const EQP_VISIBLE: u32 = EQP_HELM | EQP_GARMENT | EQP_COSTUME;

/// pc.hpp enum equip_index.
pub const EQI_ACC_L: usize = 0;
pub const EQI_ACC_R: usize = 1;
pub const EQI_SHOES: usize = 2;
pub const EQI_GARMENT: usize = 3;
pub const EQI_HEAD_LOW: usize = 4;
pub const EQI_HEAD_MID: usize = 5;
pub const EQI_HEAD_TOP: usize = 6;
pub const EQI_ARMOR: usize = 7;
pub const EQI_HAND_L: usize = 8;
pub const EQI_HAND_R: usize = 9;
pub const EQI_COSTUME_HEAD_TOP: usize = 10;
pub const EQI_COSTUME_HEAD_MID: usize = 11;
pub const EQI_COSTUME_HEAD_LOW: usize = 12;
pub const EQI_COSTUME_GARMENT: usize = 13;
pub const EQI_AMMO: usize = 14;
pub const EQI_SHADOW_ARMOR: usize = 15;
pub const EQI_SHADOW_WEAPON: usize = 16;
pub const EQI_SHADOW_SHIELD: usize = 17;
pub const EQI_SHADOW_SHOES: usize = 18;
pub const EQI_SHADOW_ACC_R: usize = 19;
pub const EQI_SHADOW_ACC_L: usize = 20;
pub const EQI_MAX: usize = 21;

/// pc.cpp equip_bitmask[EQI_MAX].
pub const EQUIP_BITMASK: [u32; EQI_MAX] = [
    EQP_ACC_L,
    EQP_ACC_R,
    EQP_SHOES,
    EQP_GARMENT,
    EQP_HEAD_LOW,
    EQP_HEAD_MID,
    EQP_HEAD_TOP,
    EQP_ARMOR,
    EQP_HAND_L,
    EQP_HAND_R,
    EQP_COSTUME_HEAD_TOP,
    EQP_COSTUME_HEAD_MID,
    EQP_COSTUME_HEAD_LOW,
    EQP_COSTUME_GARMENT,
    EQP_AMMO,
    EQP_SHADOW_ARMOR,
    EQP_SHADOW_WEAPON,
    EQP_SHADOW_SHIELD,
    EQP_SHADOW_SHOES,
    EQP_SHADOW_ACC_R,
    EQP_SHADOW_ACC_L,
];

/// pc.hpp enum e_chkitem_result.
pub const CHKADDITEM_EXIST: u8 = 0;
pub const CHKADDITEM_NEW: u8 = 1;
pub const CHKADDITEM_OVERAMOUNT: u8 = 2;

/// pc.hpp enum e_additem_result.
pub const ADDITEM_SUCCESS: u8 = 0;
pub const ADDITEM_INVALID: u8 = 1;
pub const ADDITEM_OVERWEIGHT: u8 = 2;
pub const ADDITEM_ITEM: u8 = 3;
pub const ADDITEM_OVERITEM: u8 = 4;
pub const ADDITEM_OVERAMOUNT: u8 = 5;
pub const ADDITEM_REFUSED_TIME: u8 = 6;
pub const ADDITEM_STACKLIMIT: u8 = 7;

/// clif.hpp enum clif_equipitemack_flag (PACKETVER_RE_NUM >= 20121107).
pub const ITEM_EQUIP_ACK_OK: u8 = 0;
pub const ITEM_EQUIP_ACK_FAIL: u8 = 2;
pub const ITEM_EQUIP_ACK_FAILLEVEL: u8 = 1;

/// mmo.hpp enum bound_type.
pub const BOUND_NONE: u8 = 0;
pub const BOUND_ACCOUNT: u8 = 1;
pub const BOUND_GUILD: u8 = 2;
pub const BOUND_PARTY: u8 = 3;
pub const BOUND_CHAR: u8 = 4;
pub const BOUND_MAX: u8 = 5;

/// map.hpp enum _sp (the item system's parameters).
pub const SP_ZENY: u16 = 20;
pub const SP_WEIGHT: u16 = 24;
pub const SP_MAXWEIGHT: u16 = 25;
pub const SP_CARTINFO: u16 = 99;

/// pc.hpp enum weapon_type.
pub const W_FIST: u8 = 0;
pub const W_DAGGER: u8 = 1;
pub const W_1HSWORD: u8 = 2;
pub const W_1HAXE: u8 = 6;
pub const W_2HAXE: u8 = 7;
pub const W_MACE: u8 = 8;
pub const W_STAFF: u8 = 10;
pub const W_BOW: u8 = 11;
pub const W_MUSICAL: u8 = 13;
pub const W_WHIP: u8 = 14;
pub const W_REVOLVER: u8 = 17;
pub const W_RIFLE: u8 = 18;
pub const W_GATLING: u8 = 19;
pub const W_SHOTGUN: u8 = 20;
pub const W_GRENADE: u8 = 21;
pub const W_2HSTAFF: u8 = 23;
pub const MAX_WEAPON_TYPE: u8 = 24;
pub const W_DOUBLE_DD: u8 = 25;
pub const W_DOUBLE_SS: u8 = 26;
pub const W_DOUBLE_AA: u8 = 27;
pub const W_DOUBLE_DS: u8 = 28;
pub const W_DOUBLE_DA: u8 = 29;
pub const W_DOUBLE_SA: u8 = 30;

/// status.hpp e_status_calc_weight_opt.
pub use crate::status::defs::{CALCWT_CARTSTATE, CALCWT_ITEM, CALCWT_MAXBONUS};

/// pc.hpp e_item_check (pc_check_available_item).
pub const ITMCHK_NONE: u8 = 0x0;
pub const ITMCHK_INVENTORY: u8 = 0x1;
pub const ITMCHK_CART: u8 = 0x2;
pub const ITMCHK_STORAGE: u8 = 0x4;

/// script_constants.hpp item groups (IG_*) the item code tests.
pub const IG_CASH_FOOD: u16 = 475;
pub const IG_MERCENARY: u16 = 478;
pub const IG_MF_NOTELEPORT: u16 = 1587;
pub const IG_MF_NORETURN: u16 = 1588;
pub const IG_GIANT_FLY_WING: u16 = 1589;

/// e_mapflag values (script_constants.hpp) the item code tests.
pub const MF_NOBRANCH: i16 = 3;
pub const MF_NOTRADE: i16 = 11;
pub const MF_NODROP: i16 = 38;
pub const MF_NOVENDING: i16 = 41;
pub const MF_RESET: i16 = 52;
pub const MF_NOUSECART: i16 = 54;
pub const MF_NOITEMCONSUMPTION: i16 = 55;
pub const MF_NOPETCAPTURE: i16 = 74;

// ---------------------------------------------------------------------------------------------- battle_config

/// conf/battle/items.conf `item_check: 0x0`.
pub fn item_check() -> u8 {
    crate::battle_config::get(crate::battle_config::Bc::ITEM_CHECK) as u8
}
/// conf/battle/items.conf `item_use_interval: 100`, `cashfood_use_interval: 60000`.
pub fn item_use_interval() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::ITEM_USE_INTERVAL) as i64
}
pub fn cashfood_use_interval() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::CASHFOOD_USE_INTERVAL) as i64
}
/// conf/battle/items.conf `allow_consume_restricted_item: no`, `allow_equip_restricted_item: yes`.
pub fn allow_consume_restricted_item() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ALLOW_CONSUME_RESTRICTED_ITEM) != 0
}
pub fn allow_equip_restricted_item() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ALLOW_EQUIP_RESTRICTED_ITEM) != 0
}
/// conf/battle/items.conf `default_bind_on_equip: 4`.
pub fn default_bind_on_equip() -> u8 {
    crate::battle_config::get(crate::battle_config::Bc::DEFAULT_BIND_ON_EQUIP) as u8
}
/// battle.cpp `open_box_weight_rate` (RENEWAL default 90). (natural_heal_weight_rate / major_overweight_rate: conf.rs)
pub fn open_box_weight_rate() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::OPEN_BOX_WEIGHT_RATE) as u32
}
/// conf/battle/exp.conf `disp_zeny: no` (sd->state.showzeny).
pub fn disp_zeny() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DISP_ZENY) != 0
}

// ---------------------------------------------------------------------------------------------- struct item

/// mmo.hpp struct s_item_randomoption.
#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct ItemOption {
    pub id: i16,
    pub value: i16,
    pub param: u8,
}

/// mmo.hpp struct item (the `id` column is the owning row's; `card[MAX_SLOTS]` are card0..card3; `option` always
/// has MAX_ITEM_RDM_OPT entries).
#[derive(SpacetimeType, Clone, Debug, PartialEq, Eq)]
pub struct Item {
    pub nameid: u32,
    pub amount: i16,
    /// e_equip_pos bits where the item is worn (0 = not worn).
    pub equip: u32,
    pub identify: u8,
    pub refine: u8,
    pub attribute: u8,
    pub card0: u32,
    pub card1: u32,
    pub card2: u32,
    pub card3: u32,
    pub option: Vec<ItemOption>,
    pub expire_time: u32,
    pub favorite: u8,
    pub bound: u8,
    pub unique_id: u64,
    /// e_equip_pos bits of the item in the equip switch window.
    pub equip_switch: u32,
    pub enchantgrade: u8,
}

impl Default for Item {
    fn default() -> Self {
        Item::new(0)
    }
}

impl Item {
    /// A zeroed `struct item` with this nameid (`struct item it = {}; it.nameid = …`).
    pub fn new(nameid: u32) -> Self {
        Item {
            nameid,
            amount: 0,
            equip: 0,
            identify: 0,
            refine: 0,
            attribute: 0,
            card0: 0,
            card1: 0,
            card2: 0,
            card3: 0,
            option: vec![ItemOption::default(); MAX_ITEM_RDM_OPT],
            expire_time: 0,
            favorite: 0,
            bound: 0,
            unique_id: 0,
            equip_switch: 0,
            enchantgrade: 0,
        }
    }
    pub fn card(&self, i: usize) -> u32 {
        match i {
            0 => self.card0,
            1 => self.card1,
            2 => self.card2,
            3 => self.card3,
            _ => 0,
        }
    }
    pub fn set_card(&mut self, i: usize, v: u32) {
        match i {
            0 => self.card0 = v,
            1 => self.card1 = v,
            2 => self.card2 = v,
            3 => self.card3 = v,
            _ => {}
        }
    }
    pub fn cards(&self) -> [u32; MAX_SLOTS] {
        [self.card0, self.card1, self.card2, self.card3]
    }
    pub fn opt(&self, i: usize) -> ItemOption {
        self.option.get(i).copied().unwrap_or_default()
    }
    /// The option array as rAthena holds it (MAX_ITEM_RDM_OPT entries).
    pub fn options(&self) -> [ItemOption; MAX_ITEM_RDM_OPT] {
        let mut o = [ItemOption::default(); MAX_ITEM_RDM_OPT];
        for (i, e) in o.iter_mut().enumerate() {
            *e = self.opt(i);
        }
        o
    }
}

// ---------------------------------------------------------------------------------------------- tables

/// rAthena `inventory` table (sql-files/main.sql) + the session index of the slot.
#[table(accessor = inventory, index(accessor = by_char_idx, btree(columns = [char_id, idx])))]
#[derive(Clone, Debug)]
pub struct InventoryItem {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    #[index(btree)]
    pub char_id: u32,
    /// sd->inventory.u.items_inventory index (0 .. MAX_INVENTORY-1).
    pub idx: u16,
    /// The SQL `id` of the row as char_memitemdata_to_sql last wrote it (0: not saved yet); the load order.
    pub save_id: u64,
    pub item: Item,
}

/// map_session_data item state (one row per online character, key = account id).
#[table(accessor = pc_items)]
#[derive(Clone, Debug)]
pub struct PcItems {
    #[primary_key]
    pub account_id: u32,
    pub char_id: u32,
    /// sd->weight / sd->max_weight (status_calc_weight).
    pub weight: u32,
    pub max_weight: u32,
    /// sd->add_max_weight (bAddMaxWeight; set by the status calculation).
    pub add_max_weight: u32,
    /// sd->cart_weight / cart_num / cart_weight_max (status_calc_cart_weight).
    pub cart_weight: u32,
    pub cart_num: u16,
    pub cart_weight_max: u32,
    /// sd->equip_index[EQI_MAX] / equip_switch_index[EQI_MAX] (-1 = none).
    pub equip_index: Vec<i16>,
    pub equip_switch_index: Vec<i16>,
    /// sd->weapontype1 / weapontype2.
    pub weapontype1: u8,
    pub weapontype2: u8,
    pub canuseitem_tick: Tick,
    pub canusecashfood_tick: Tick,
    pub canequip_tick: Tick,
    pub equipswitch_tick: Tick,
    /// sd->item_delay[MAX_ITEMDELAYS] (nameid, tick).
    pub item_delay: Vec<ItemDelay>,
    /// sd->itemid / sd->itemindex: the item being used (script commands read them).
    pub itemid: u32,
    pub itemindex: i16,
    pub last_addeditem_index: i16,
    /// sd->combos (id, pos).
    pub combos: Vec<PcCombo>,
    pub rental_timer: u64,
    /// sd->storage.amount / max_amount / dirty (the account storage as loaded at login).
    pub storage_amount: u16,
    pub storage_max: u16,
    pub storage_dirty: bool,
    /// sd->setlook_head_* / setlook_robe (setlook script command).
    pub setlook_head_bottom: i32,
    pub setlook_head_mid: i32,
    pub setlook_head_top: i32,
    pub setlook_robe: i32,
}

#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct ItemDelay {
    pub nameid: u32,
    pub tick: Tick,
}

#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct PcCombo {
    pub id: u16,
    pub pos: u32,
}

/// pc.cpp itemcd_db: item cooldowns kept between logins while the server runs (by char id).
#[table(accessor = item_cd)]
#[derive(Clone, Debug)]
pub struct ItemCd {
    #[primary_key]
    pub char_id: u32,
    pub delays: Vec<ItemDelay>,
}

/// Run-time changes to item_data (flag.autoequip from the `autoequip` script command).
#[table(accessor = itemdb_runtime)]
#[derive(Clone, Debug)]
pub struct ItemdbRuntime {
    #[primary_key]
    pub nameid: u32,
    pub autoequip: bool,
}

// ---------------------------------------------------------------------------------------------- access

pub fn st(c: &Ctx, aid: u32) -> Option<PcItems> {
    c.db().pc_items().account_id().find(aid)
}

pub fn put_st(c: &Ctx, s: &PcItems) {
    c.db().pc_items().account_id().update(s.clone());
}

fn char_id_of(c: &Ctx, aid: u32) -> Option<u32> {
    pc::get(c, aid).map(|p| p.char_id)
}

/// sd->status (the character row).
pub fn status(c: &Ctx, aid: u32) -> Option<Character> {
    pc::character_of(c, aid)
}

pub fn put_status(c: &Ctx, ch: &Character) {
    c.db().character().id().update(ch.clone());
}

/// sd->inventory.u.items_inventory[idx] (None: nameid 0).
pub fn inv_get(c: &Ctx, aid: u32, idx: usize) -> Option<InventoryItem> {
    if idx >= MAX_INVENTORY {
        return None;
    }
    let cid = char_id_of(c, aid)?;
    c.db().inventory().by_char_idx().filter((cid, idx as u16)).next()
}

/// Every occupied slot, by index.
pub fn inv_list(c: &Ctx, aid: u32) -> Vec<InventoryItem> {
    let Some(cid) = char_id_of(c, aid) else { return Vec::new() };
    inv_list_char(c, cid)
}

pub fn inv_list_char(c: &Ctx, cid: u32) -> Vec<InventoryItem> {
    let mut v: Vec<InventoryItem> = c.db().inventory().char_id().filter(cid).collect();
    v.sort_by_key(|r| r.idx);
    v
}

/// The slot array as rAthena holds it (None = empty slot).
pub fn inv_array(c: &Ctx, aid: u32) -> Vec<Option<InventoryItem>> {
    let mut a: Vec<Option<InventoryItem>> = vec![None; MAX_INVENTORY];
    for r in inv_list(c, aid) {
        if (r.idx as usize) < MAX_INVENTORY {
            let i = r.idx as usize;
            a[i] = Some(r);
        }
    }
    a
}

pub fn inv_put(c: &Ctx, r: &InventoryItem) {
    c.db().inventory().id().update(r.clone());
}

/// sd->inventory_data[idx]: the item's database entry.
pub fn data_of(it: &Item) -> &'static ItemData {
    itemdb::itemdb_search(it.nameid)
}

/// itemdb_autoequip.
pub fn itemdb_autoequip(c: &Ctx, nameid: u32) -> bool {
    c.db().itemdb_runtime().nameid().find(nameid).map(|r| r.autoequip).unwrap_or(false)
}

pub fn set_itemdb_autoequip(c: &Ctx, nameid: u32, on: bool) {
    let row = ItemdbRuntime { nameid, autoequip: on };
    if c.db().itemdb_runtime().nameid().find(nameid).is_some() {
        c.db().itemdb_runtime().nameid().update(row);
    } else {
        c.db().itemdb_runtime().insert(row);
    }
}

/// pc.hpp pc_jobid2mapid on the character's class (sd->class_).
pub fn class_mapid(ch: &Character) -> u64 {
    crate::db::jobs::pc_jobid2mapid(ch.class)
}

/// pc_can_give_items.
pub fn pc_can_give_items(c: &Ctx, aid: u32) -> bool {
    pc::has_permission(c, aid, crate::rules::PC_PERM_TRADE) || pc::has_permission(c, aid, crate::rules::PC_PERM_TRADE_UNCONDITIONAL)
}

/// pc_can_give_bounded_items.
pub fn pc_can_give_bounded_items(c: &Ctx, aid: u32) -> bool {
    pc::has_permission(c, aid, crate::rules::PC_PERM_TRADE_BOUNDED) || pc::has_permission(c, aid, crate::rules::PC_PERM_TRADE_UNCONDITIONAL)
}

/// pc.cpp pc_can_trade_item: not a rental, not bound (or bound items may be given), and tradable by the item db.
pub fn pc_can_trade_item(c: &Ctx, aid: u32, it: &Item) -> bool {
    let gmlv = pc::group_level(c, aid);
    it.expire_time == 0
        && (it.bound == 0 || pc_can_give_bounded_items(c, aid))
        && itemdb::itemdb_isrestricted(it.nameid, &it.cards(), gmlv, gmlv, itemdb::itemdb_cantrade_sub)
}

/// pc.cpp pc_generate_unique_id: char id << 32 | uniqueitem_counter++ (the counter is saved with the character).
pub fn pc_generate_unique_id(c: &Ctx, aid: u32) -> u64 {
    let Some(mut ch) = status(c, aid) else { return 0 };
    let id = ((ch.id as u64) << 32) | ch.uniqueitem_counter as u64;
    ch.uniqueitem_counter = ch.uniqueitem_counter.wrapping_add(1);
    put_status(c, &ch);
    id
}

/// pc.cpp pc_bound_chk: the inventory slots whose item is bound as `bound`, in slot order.
pub fn pc_bound_chk(c: &Ctx, aid: u32, bound: u8) -> Vec<usize> {
    inv_array(c, aid)
        .iter()
        .enumerate()
        .filter(|(_, r)| r.as_ref().map(|r| r.item.nameid > 0 && r.item.amount > 0 && r.item.bound == bound).unwrap_or(false))
        .map(|(i, _)| i)
        .collect()
}

/// pc.hpp pc_issit.
pub fn pc_issit(c: &Ctx, aid: u32) -> bool {
    unit::get(c, aid).map(|u| u.vd.dead_sit == 2).unwrap_or(false)
}

/// pc.hpp pc_istrading.
pub fn pc_istrading(p: &pc::Pc) -> bool {
    p.npc_id != 0 || p.vending || p.buyingstore || p.trading
}

/// pc.hpp pc_cant_act2 (the item windows, the barter windows included, are `ui_open` bits).
pub fn pc_cant_act2(p: &pc::Pc, opt1: i16) -> bool {
    const OPT1_BURNING: i16 = 7;
    p.vending || p.buyingstore || (opt1 != 0 && opt1 != OPT1_BURNING) || p.trading || p.storage_flag != 0 || p.prevend || p.npc_shopid != 0 || p.ui_open != 0
}

/// pc.hpp pc_iscarton (NEW_CARTS: the status change SC_PUSH_CART, val1 = the cart type).
pub fn pc_iscarton(c: &Ctx, aid: u32) -> bool {
    crate::status::sc::has_sce(c, aid, crate::status::consts::SC_PUSH_CART)
}

// ---------------------------------------------------------------------------------------------- session start / end

fn new_state(aid: u32, char_id: u32) -> PcItems {
    PcItems {
        account_id: aid,
        char_id,
        weight: 0,
        max_weight: 0,
        add_max_weight: 0,
        cart_weight: 0,
        cart_num: 0,
        cart_weight_max: 0,
        equip_index: vec![-1; EQI_MAX],
        equip_switch_index: vec![-1; EQI_MAX],
        weapontype1: W_FIST,
        weapontype2: W_FIST,
        canuseitem_tick: 0,
        canusecashfood_tick: 0,
        canequip_tick: 0,
        equipswitch_tick: 0,
        item_delay: vec![ItemDelay::default(); MAX_ITEMDELAYS],
        itemid: 0,
        itemindex: -1,
        last_addeditem_index: -1,
        combos: Vec::new(),
        rental_timer: 0,
        storage_amount: 0,
        storage_max: 0,
        storage_dirty: false,
        setlook_head_bottom: 0,
        setlook_head_mid: 0,
        setlook_head_top: 0,
        setlook_robe: 0,
    }
}

/// pc_authok's `memset(&sd->inventory …)`, `memset(&sd->equip_index, -1 …)` and `sd->last_addeditem_index = -1`:
/// the item state of a character entering the world.
pub fn on_authok(c: &Ctx, aid: u32, char_id: u32) {
    let row = new_state(aid, char_id);
    if c.db().pc_items().account_id().find(aid).is_some() {
        c.db().pc_items().account_id().update(row);
    } else {
        c.db().pc_items().insert(row);
    }
}

/// char_memitemdata_from_sql(TABLE_INVENTORY): the rows are put into slots 0.. in their SQL id order.
fn inventory_fromsql(c: &Ctx, char_id: u32) {
    let mut rows: Vec<InventoryItem> = c.db().inventory().char_id().filter(char_id).collect();
    // rows written since the last save have no SQL id yet: they would have been inserted then (in slot order)
    rows.sort_by_key(|r| (r.save_id == 0, r.save_id, r.idx, r.id));
    for (i, mut r) in rows.into_iter().enumerate() {
        if i >= MAX_INVENTORY {
            break;
        }
        if r.idx != i as u16 {
            r.idx = i as u16;
            c.db().inventory().id().update(r);
        }
    }
}

/// intif_parse_StorageReceived for TABLE_INVENTORY (the storage and cart arrive first: pc_reg_received requests
/// storage, cart, inventory in that order): pc_setinventorydata, pc_setequipindex, pc_check_expiration,
/// pc_check_available_item, pc_itemcd_do(load), status_set_viewdata's weapon / shield look, pc_load_combo,
/// status_calc_pc(SCO_FIRST|SCO_FORCE), status_calc_weight(CALCWT_ITEM|CALCWT_MAXBONUS).
pub fn inventory_received(c: &Ctx, aid: u32) {
    let Some(p) = pc::get(c, aid) else { return };
    // TABLE_STORAGE, TABLE_CART
    crate::storage::storage_received(c, aid);
    crate::cart::cart_received(c, aid);
    inventory_fromsql(c, p.char_id);
    // pc_setinventorydata: inventory_data[] is itemdb_search of each slot (data_of)
    crate::pc_equip::pc_setequipindex(c, aid);
    pc_check_expiration(c, aid);
    pc_check_available_item(c, aid, ITMCHK_INVENTORY);
    pc_itemcd_do(c, aid, true);
    // Party bound item check (BOUND_ITEMS, intif.cpp:3490-3497): the party was deleted while the character was offline
    if status(c, aid).map(|ch| ch.party_id == 0).unwrap_or(false) {
        for i in pc_bound_chk(c, aid, BOUND_PARTY) {
            let amount = inv_get(c, aid, i).map(|r| r.item.amount as i32).unwrap_or(0);
            pc_delitem(c, aid, i as i32, amount, 4, 1, LOG_TYPE_OTHER);
        }
    }
    // status_set_viewdata: the weapon / shield sprites need the inventory
    crate::pc_equip::status_set_viewdata_items(c, aid);
    crate::pc_equip::pc_load_combo(c, aid);
    crate::status::calc_pc(c, aid, crate::status::SCO_FIRST | crate::status::SCO_FORCE);
    status_calc_weight(c, aid, CALCWT_ITEM | CALCWT_MAXBONUS);
}

/// map_quit's `for (i = 0; i < EQI_MAX; i++) if (pc_isequip(...) != ITEM_EQUIP_ACK_OK) pc_unequipitem(sd, i, 2)`.
pub fn map_quit_equip(c: &Ctx, aid: u32) {
    for i in 0..EQI_MAX {
        let idx = st(c, aid).map(|s| s.equip_index[i]).unwrap_or(-1);
        if idx >= 0 && crate::pc_equip::pc_isequip(c, aid, idx as usize) != ITEM_EQUIP_ACK_OK {
            crate::pc_equip::pc_unequipitem(c, aid, idx as i32, 2);
        }
    }
}

/// chrif_save(CSAVE_INVENTORY | CSAVE_CART) at map_quit: the storage, inventory and cart are written back
/// (storage_storagesave / intif_storage_save → char_memitemdata_to_sql); the rental timer stops
/// (pc_itemcd_do(false) ran before the logout event). SC_PUSH_CART is saved with the other status changes
/// (status.rs on_quit_save).
pub fn on_quit(c: &Ctx, aid: u32) {
    let Some(s) = st(c, aid) else { return };
    if s.rental_timer != 0 {
        crate::timer::delete(c, s.rental_timer);
    }
    crate::storage::chrif_save_items(c, aid);
}

/// unit_free's item part: the session state goes (the barter window flags are `pc.ui_open` bits and go with the
/// `pc` row).
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_items().account_id().delete(aid);
}

/// char.cpp char_memitemdata_to_sql for the inventory: SQL rows (in id order) are matched with the first unmatched
/// slot that has the same nameid, card0, card2, card3 and unique_id; the slot takes the row's id, rows without a
/// slot are deleted, and the remaining slots are inserted in slot order (new ids).
pub fn inventory_tosql(c: &Ctx, char_id: u32) {
    let rows = inv_list_char(c, char_id);
    let items: Vec<(u64, u64, Item)> = rows.iter().map(|r| (r.id, r.save_id, r.item.clone())).collect();
    let assigned = memitemdata_to_sql(c, &items);
    for r in rows {
        if let Some(&sid) = assigned.get(&r.id) {
            if r.save_id != sid {
                inv_put(c, &InventoryItem { save_id: sid, ..r });
            }
        }
    }
}

/// char_memitemdata_to_sql's matching over (row id, current SQL id, item) in slot order: returns row id → SQL id.
/// The "SQL table" is the set of SQL ids the rows carried (a saved row keeps its id while its slot still matches).
pub fn memitemdata_to_sql(c: &Ctx, items: &[(u64, u64, Item)]) -> std::collections::HashMap<u64, u64> {
    let mut out = std::collections::HashMap::new();
    // the SQL table as it was after the last save: every saved row (by SQL id) with the item it held then — here the
    // item the row holds now, matched on the save's key fields
    let mut db: Vec<(u64, Item)> = items.iter().filter(|(_, sid, _)| *sid != 0).map(|(_, sid, it)| (*sid, it.clone())).collect();
    db.sort_by_key(|(sid, _)| *sid);
    let mut flag = vec![false; items.len()];
    for (sid, dbit) in &db {
        let found = items.iter().enumerate().find(|(i, (_, _, it))| {
            !flag[*i]
                && it.nameid != 0
                && it.nameid == dbit.nameid
                && it.card0 == dbit.card0
                && it.card2 == dbit.card2
                && it.card3 == dbit.card3
                && it.unique_id == dbit.unique_id
        });
        if let Some((i, (rid, _, _))) = found {
            flag[i] = true;
            out.insert(*rid, *sid);
        }
        // (no slot: the SQL row is deleted)
    }
    for (i, (rid, _, it)) in items.iter().enumerate() {
        if it.nameid == 0 || flag[i] {
            continue;
        }
        out.insert(*rid, crate::ids::next(c.rc, "item_sql_id", 1) as u64);
    }
    out
}

// ---------------------------------------------------------------------------------------------- lookups

/// pc.cpp pc_search_inventory: the first slot holding `nameid` (with amount > 0; nameid 0 = the first empty slot).
pub fn pc_search_inventory(c: &Ctx, aid: u32, nameid: u32) -> i16 {
    let a = inv_array(c, aid);
    for (i, s) in a.iter().enumerate() {
        match s {
            Some(r) if r.item.nameid == nameid && r.item.amount > 0 => return i as i16,
            None if nameid == 0 => return i as i16,
            _ => {}
        }
    }
    -1
}

/// pc.cpp pc_checkadditem.
pub fn pc_checkadditem(c: &Ctx, aid: u32, nameid: u32, amount: i32) -> u8 {
    if amount > MAX_AMOUNT {
        return CHKADDITEM_OVERAMOUNT;
    }
    let data = itemdb::itemdb_search(nameid);
    if !data.is_stackable() {
        return CHKADDITEM_NEW;
    }
    if data.stack.inventory && amount > data.stack.amount as i32 {
        return CHKADDITEM_OVERAMOUNT;
    }
    if data.flag.guid {
        return CHKADDITEM_NEW;
    }
    let slots = status(c, aid).map(|ch| ch.inventory_slots).unwrap_or(INVENTORY_BASE_SIZE);
    for r in inv_list(c, aid) {
        // FIXME (rAthena): this does not consider the checked item's cards
        if r.item.nameid == nameid {
            let have = r.item.amount as i32;
            if amount > MAX_AMOUNT - have || (data.stack.inventory && amount > data.stack.amount as i32 - have) {
                return CHKADDITEM_OVERAMOUNT;
            }
            if r.idx as i32 >= slots as i32 {
                return CHKADDITEM_OVERAMOUNT;
            }
            return CHKADDITEM_EXIST;
        }
    }
    CHKADDITEM_NEW
}

/// pc.cpp pc_inventoryblank: empty slots among the first inventory_slots.
pub fn pc_inventoryblank(c: &Ctx, aid: u32) -> u16 {
    let slots = status(c, aid).map(|ch| ch.inventory_slots.max(0) as usize).unwrap_or(INVENTORY_BASE_SIZE as usize);
    let a = inv_array(c, aid);
    let mut b = 0u16;
    for s in a.iter().take(slots.min(MAX_INVENTORY)) {
        if s.is_none() {
            b = b.wrapping_add(1);
        }
    }
    // uint8 in rAthena
    b & 0xff
}

// ---------------------------------------------------------------------------------------------- weight / zeny

/// clif.cpp clif_updatestatus (status/clif.rs), for the item system's SP_* constants.
pub fn updatestatus(c: &Ctx, aid: u32, sp: u16) {
    crate::status::clif::updatestatus(c, aid, sp as i32);
}

/// status.cpp status_calc_weight (status/calc.rs).
pub use crate::status::calc::{pc_getpercentweight, pc_updateweightstatus, status_calc_cart_weight, status_calc_weight};

/// pc.cpp pc_payzeny: 0 success, 1 not enough / negative, 2 no player. `type_`: the zeny log's type; `log_charid`: from
/// whom to log (0 if not needed; rAthena's default argument).
pub fn pc_payzeny(c: &Ctx, aid: u32, zeny: i64, type_: LogPickType, log_charid: u32) -> i32 {
    let Some(mut ch) = status(c, aid) else { return 2 };
    let zeny = zeny.clamp(-MAX_ZENY, MAX_ZENY);
    if zeny < 0 {
        log::error!("pc_payzeny: Paying negative Zeny (zeny={zeny}, account_id={aid}, char_id={}).", ch.id);
        return 1;
    }
    if (ch.zeny as i64) < zeny {
        return 1;
    }
    ch.zeny = (ch.zeny as i64 - zeny) as u32;
    put_status(c, &ch);
    updatestatus(c, aid, SP_ZENY);
    crate::map_log::log_zeny(c, aid, type_, log_charid, -(zeny as i32));
    if zeny > 0 && crate::atcmd_pc::get(c, aid).showzeny {
        if let Some(sd) = unit::get(c, aid) {
            clif::messagecolor_target(c, &sd, crate::atcmd_pc::COLOR_LIGHT_GREEN, format!("Removed {}z.", zeny as i32).as_bytes(), false, Target::SelfOnly, None);
        }
    }
    0
}

/// pc.cpp pc_getzeny: 0 success, 1 negative, -1 no player. `type_`: the zeny log's type; `log_charid`: from whom to
/// log (0 if not needed; rAthena's default argument).
pub fn pc_getzeny(c: &Ctx, aid: u32, zeny: i64, type_: LogPickType, log_charid: u32) -> i32 {
    let Some(mut ch) = status(c, aid) else { return -1 };
    let mut zeny = zeny.clamp(-MAX_ZENY, MAX_ZENY);
    if zeny < 0 {
        log::error!("pc_getzeny: Obtaining negative Zeny (zeny={zeny}, account_id={aid}, char_id={}).", ch.id);
        return 1;
    }
    if zeny > MAX_ZENY - ch.zeny as i64 {
        zeny = MAX_ZENY - ch.zeny as i64;
    }
    ch.zeny = (ch.zeny as i64 + zeny) as u32;
    put_status(c, &ch);
    updatestatus(c, aid, SP_ZENY);
    crate::map_log::log_zeny(c, aid, type_, log_charid, zeny as i32);
    if zeny > 0 && crate::atcmd_pc::get(c, aid).showzeny {
        if let Some(sd) = unit::get(c, aid) {
            clif::messagecolor_target(c, &sd, crate::atcmd_pc::COLOR_LIGHT_GREEN, format!("Gained {}z.", zeny as i32).as_bytes(), false, Target::SelfOnly, None);
        }
    }
    let total = status(c, aid).map(|ch| ch.zeny as i32).unwrap_or(0);
    crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_GET_ZENY, &[total]);
    0
}

// ---------------------------------------------------------------------------------------------- add / delete

/// pc.cpp pc_additem. `item` may get a unique id (the caller's struct is changed, as in rAthena). `log_type`: the item
/// log's type (log.rs log_pick_pc).
pub fn pc_additem(c: &Ctx, aid: u32, item: &mut Item, amount: i32, log_type: LogPickType) -> u8 {
    pc_additem_fav(c, aid, item, amount, log_type, false)
}

/// pc.cpp pc_additem with its `favorite` argument (storage_storageget / storage_guild_storageget).
pub fn pc_additem_fav(c: &Ctx, aid: u32, item: &mut Item, amount: i32, log_type: LogPickType, favorite: bool) -> u8 {
    let Some(s) = st(c, aid) else { return ADDITEM_INVALID };
    let Some(ch) = status(c, aid) else { return ADDITEM_INVALID };
    if item.nameid == 0 || amount <= 0 {
        return ADDITEM_INVALID;
    }
    if amount > MAX_AMOUNT {
        return ADDITEM_OVERAMOUNT;
    }
    let id = itemdb::itemdb_search(item.nameid);
    if id.stack.inventory && amount > id.stack.amount as i32 {
        return ADDITEM_STACKLIMIT;
    }
    let w = id.weight.wrapping_mul(amount as u32);
    if s.weight as u64 + w as u64 > s.max_weight as u64 {
        return ADDITEM_OVERWEIGHT;
    }
    if id.flag.guid && item.unique_id == 0 {
        item.unique_id = pc_generate_unique_id(c, aid);
    }
    let slots = ch.inventory_slots as i32;
    let a = inv_array(c, aid);
    let mut i = MAX_INVENTORY;
    if id.is_stackable() && item.expire_time == 0 {
        for (k, slot) in a.iter().enumerate() {
            let Some(r) = slot else { continue };
            let it = &r.item;
            if it.nameid == item.nameid && it.bound == item.bound && it.expire_time == 0 && it.unique_id == item.unique_id && it.cards() == item.cards() {
                if amount > MAX_AMOUNT - it.amount as i32 || (id.stack.inventory && amount > id.stack.amount as i32 - it.amount as i32) {
                    return ADDITEM_OVERAMOUNT;
                }
                if k as i32 >= slots {
                    return ADDITEM_OVERAMOUNT;
                }
                let mut r2 = r.clone();
                r2.item.amount += amount as i16;
                inv_put(c, &r2);
                crate::clif_items::additem(c, aid, k as i32, amount, 0);
                i = k;
                break;
            }
        }
    }
    if i >= MAX_INVENTORY {
        let Some(k) = a.iter().position(|s| s.is_none()) else { return ADDITEM_OVERITEM };
        if k as i32 >= slots {
            return ADDITEM_OVERITEM;
        }
        let mut it = item.clone();
        // clear equip and equip switch fields first, just in case
        if item.equip != 0 {
            it.equip = 0;
        }
        if item.equip_switch != 0 {
            it.equip_switch = 0;
        }
        it.amount = amount as i16;
        it.favorite = favorite as u8;
        if !id.is_stackable() || id.flag.guid {
            it.unique_id = if item.unique_id != 0 { item.unique_id } else { pc_generate_unique_id(c, aid) };
        }
        let cid = ch.id;
        c.db().inventory().insert(InventoryItem { id: 0, char_id: cid, idx: k as u16, save_id: 0, item: it });
        // the memcpy over the slot (pc.cpp:6049) also overwrites a favorite flag left on the empty slot
        crate::clif_client::slot_filled(c, aid, k);
        if let Some(mut s2) = st(c, aid) {
            s2.last_addeditem_index = k as i16;
            put_st(c, &s2);
        }
        crate::clif_items::additem(c, aid, k as i32, amount, 0);
        i = k;
    }
    if let Some(r) = inv_get(c, aid, i) {
        crate::map_log::log_pick_pc(c, aid, log_type, amount, &r.item);
    }
    if let Some(mut s2) = st(c, aid) {
        s2.weight = s2.weight.wrapping_add(w);
        put_st(c, &s2);
    }
    updatestatus(c, aid, SP_WEIGHT);
    // Auto-equip
    if itemdb_autoequip(c, id.nameid) {
        crate::pc_equip::pc_equipitem(c, aid, i as i16, id.equip, false);
    }
    // rental item check
    if item.expire_time != 0 {
        let now = c.tick() / 1000;
        let nameid = inv_get(c, aid, i).map(|r| r.item.nameid).unwrap_or(item.nameid);
        if now > item.expire_time as i64 {
            crate::clif_items::rental_expired(c, aid, i as i32, nameid);
            let amt = inv_get(c, aid, i).map(|r| r.item.amount as i32).unwrap_or(0);
            pc_delitem(c, aid, i as i32, amt, 1, 0, LOG_TYPE_OTHER);
        } else {
            let seconds = (item.expire_time as i64 - now) as u32;
            crate::clif_items::rental_time(c, aid, nameid, seconds as i32);
            pc_inventory_rental_add(c, aid, seconds);
        }
    }
    crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_GET_ITEM, &[id.value_sell as i32]);
    crate::quest::pc_show_questinfo(c, aid);
    ADDITEM_SUCCESS
}

/// pc.cpp pc_delitem. type &1: no ZC_DELETE_ITEM_FROM_BODY, &2: no weight update, &4: no status recalculation.
/// `log_type`: the item log's type (log.rs log_pick_pc). Returns 1 for an invalid slot / amount, 0 on success.
pub fn pc_delitem(c: &Ctx, aid: u32, n: i32, amount: i32, kind: i32, reason: i16, log_type: LogPickType) -> i32 {
    if n < 0 || n as usize >= MAX_INVENTORY {
        return 1;
    }
    let Some(mut r) = inv_get(c, aid, n as usize) else { return 1 };
    if r.item.nameid == 0 || amount <= 0 || (r.item.amount as i32) < amount {
        return 1;
    }
    let data = data_of(&r.item);
    crate::map_log::log_pick_pc(c, aid, log_type, -amount, &r.item);
    r.item.amount -= amount as i16;
    inv_put(c, &r);
    if let Some(mut s) = st(c, aid) {
        s.weight = s.weight.wrapping_sub(data.weight.wrapping_mul(amount as u32));
        put_st(c, &s);
    }
    if r.item.amount <= 0 {
        if r.item.equip != 0 {
            crate::pc_equip::pc_unequipitem(c, aid, n, 2 | if kind & 4 == 0 { 1 } else { 0 });
        }
        c.db().inventory().id().delete(r.id);
    }
    if kind & 1 == 0 {
        crate::clif_items::delitem(c, aid, n, amount, reason);
    }
    if kind & 2 == 0 {
        updatestatus(c, aid, SP_WEIGHT);
    }
    crate::quest::pc_show_questinfo(c, aid);
    0
}

// ---------------------------------------------------------------------------------------------- checks at load

/// pc.cpp pc_check_available_item (item_check: 0x0 — nothing is checked on this server's configuration).
pub fn pc_check_available_item(c: &Ctx, aid: u32, kind: u8) {
    let Some(sd) = unit::get(c, aid) else { return };
    if item_check() & ITMCHK_INVENTORY != 0 && kind & ITMCHK_INVENTORY != 0 {
        for r in inv_list(c, aid) {
            let nameid = r.item.nameid;
            if !itemdb::itemdb_available(nameid) {
                clif::displaymessage(c, &sd, &crate::data::format_msg(crate::data::msg_txt(709), &[&nameid]));
                log::warn!("Removed invalid/disabled item (ID: {nameid}, amount: {}) from inventory (char_id: {}).", r.item.amount, r.char_id);
                pc_delitem(c, aid, r.idx as i32, r.item.amount as i32, 4, 0, LOG_TYPE_OTHER);
                continue;
            }
            if r.item.unique_id == 0 && !itemdb::itemdb_isstackable(nameid) {
                let uid = pc_generate_unique_id(c, aid);
                if let Some(mut r2) = inv_get(c, aid, r.idx as usize) {
                    r2.item.unique_id = uid;
                    inv_put(c, &r2);
                }
            }
        }
    }
    if item_check() & ITMCHK_CART != 0 && kind & ITMCHK_CART != 0 {
        crate::cart::check_available(c, aid);
    }
    if item_check() & ITMCHK_STORAGE != 0 && kind & ITMCHK_STORAGE != 0 {
        crate::storage::check_available(c, aid);
    }
}

/// pc.cpp pc_check_expiration (ENABLE_SC_SAVING is on, so the rentals are checked in pc_scdata_received; accounts
/// here have no expiration time, unless the database's owner wrote one: `login_ext.expiration_time` refuses the
/// login when it has passed, atcommand_accounts.rs; the notice and the timer of an account that expires while its
/// character plays are not ported).
pub fn pc_check_expiration(_c: &Ctx, _aid: u32) {}

/// pc.cpp pc_inventory_rentals: expired rentals are deleted, the others announced; the rental timer is armed.
pub fn pc_inventory_rentals(c: &Ctx, aid: u32) {
    let now = c.tick() / 1000;
    let mut count = 0;
    let mut next_tick: u32 = u32::MAX;
    for r in inv_list(c, aid) {
        if r.item.nameid == 0 || r.item.expire_time == 0 {
            continue;
        }
        if r.item.expire_time as i64 <= now {
            if data_of(&r.item).scripts().and_then(|s| s.unequip_script.as_ref()).is_some() {
                let fake = crate::script::fake_npc_id(c);
                run_item_script(c, aid, r.item.nameid, ItemScript::Unequip, fake);
            }
            crate::clif_items::rental_expired(c, aid, r.idx as i32, r.item.nameid);
            pc_delitem(c, aid, r.idx as i32, r.item.amount as i32, 0, 0, LOG_TYPE_OTHER);
        } else {
            let expire_tick = (r.item.expire_time as i64 - now) as u32;
            crate::clif_items::rental_time(c, aid, r.item.nameid, expire_tick as i32);
            next_tick = next_tick.min(expire_tick.wrapping_mul(1000));
            count += 1;
        }
    }
    let Some(mut s) = st(c, aid) else { return };
    s.rental_timer = if count > 0 { crate::timer::add(c, c.tick() + next_tick.min(3_600_000) as i64, crate::timer::Func::PcInventoryRental, aid, 0) } else { 0 };
    put_st(c, &s);
}

/// pc.cpp pc_inventory_rental_add.
pub fn pc_inventory_rental_add(c: &Ctx, aid: u32, seconds: u32) {
    let Some(mut s) = st(c, aid) else { return };
    let tick = seconds as i64 * 1000;
    if s.rental_timer != 0 {
        if let Some((due, _)) = crate::timer::get(c, s.rental_timer) {
            if due - c.tick() > tick {
                crate::timer::delete(c, s.rental_timer);
                s.rental_timer = crate::timer::add(c, c.tick() + tick, crate::timer::Func::PcInventoryRental, aid, 0);
            }
        }
    } else {
        s.rental_timer = crate::timer::add(c, c.tick() + tick.min(3_600_000), crate::timer::Func::PcInventoryRental, aid, 0);
    }
    put_st(c, &s);
}

/// pc.cpp pc_inventory_rental_end (timer).
pub fn pc_inventory_rental_end(c: &Ctx, tid: u64, aid: u32) {
    let Some(mut s) = st(c, aid) else { return };
    if tid != s.rental_timer {
        log::error!("pc_inventory_rental_end: invalid timer id {tid}, expected {}", s.rental_timer);
        return;
    }
    s.rental_timer = 0;
    put_st(c, &s);
    pc_inventory_rentals(c, aid);
}

// ---------------------------------------------------------------------------------------------- item cooldowns

/// pc.cpp pc_itemcd_do: load (at login) or keep (at logout) the item cooldowns still running.
pub fn pc_itemcd_do(c: &Ctx, aid: u32, load: bool) {
    let Some(mut s) = st(c, aid) else { return };
    let cid = s.char_id;
    if load {
        let Some(cd) = c.db().item_cd().char_id().find(cid) else { return };
        let mut cursor = 0;
        for d in &cd.delays {
            if d.nameid != 0 && c.tick() - d.tick < 0 && cursor < MAX_ITEMDELAYS {
                s.item_delay[cursor] = *d;
                cursor += 1;
            }
        }
        put_st(c, &s);
        c.db().item_cd().char_id().delete(cid);
    } else {
        let mut delays = vec![ItemDelay::default(); MAX_ITEMDELAYS];
        if let Some(cd) = c.db().item_cd().char_id().find(cid) {
            delays = cd.delays.clone();
            delays.resize(MAX_ITEMDELAYS, ItemDelay::default());
        }
        let mut cursor = 0;
        for d in &s.item_delay {
            if d.nameid != 0 && c.tick() - d.tick < 0 {
                delays[cursor] = *d;
                cursor += 1;
            }
        }
        let row = ItemCd { char_id: cid, delays };
        if c.db().item_cd().char_id().find(cid).is_some() {
            c.db().item_cd().char_id().update(row);
        } else {
            c.db().item_cd().insert(row);
        }
    }
}

/// pc.cpp pc_itemcd_add: 1 = still cooling down (use refused).
fn pc_itemcd_add(c: &Ctx, aid: u32, id: &ItemData, tick: Tick, _n: usize) -> u8 {
    let Some(mut s) = st(c, aid) else { return 0 };
    let mut i = s.item_delay.iter().position(|d| d.nameid == id.nameid).unwrap_or(MAX_ITEMDELAYS);
    if i == MAX_ITEMDELAYS {
        i = s.item_delay.iter().position(|d| d.nameid == 0).unwrap_or(MAX_ITEMDELAYS);
    }
    if i < MAX_ITEMDELAYS {
        if s.item_delay[i].nameid != 0 {
            if s.item_delay[i].tick - tick > 0 {
                let e_tick = (s.item_delay[i].tick - tick) / 1000;
                let name = itemdb::itemdb_ename(s.item_delay[i].nameid);
                let e_msg = if e_tick > 99 {
                    // Item Failed. [%s] is cooling down. Wait %.1f minutes.
                    crate::data::format_msg(crate::data::msg_txt(379), &[&name, &format!("{:.1}", e_tick as f64 / 60.0)])
                } else {
                    // Item Failed. [%s] is cooling down. Wait %d seconds.
                    crate::data::format_msg(crate::data::msg_txt(380), &[&name, &(e_tick + 1)])
                };
                if let Some(sd) = unit::get(c, aid) {
                    // color_table[COLOR_YELLOW]: 0xFFFF00 turned BGR by do_init_clif (clif.cpp:25873)
                    clif::messagecolor_target(c, &sd, 0x00ffff, &crate::net::wire::latin1(&e_msg), false, Target::SelfOnly, None);
                }
                return 1;
            }
        } else {
            s.item_delay[i].nameid = id.nameid;
        }
        const ITEMID_REINS_OF_MOUNT: u32 = 12622;
        const RIDE_OPTIONS: i32 = 0x20_0000 | 0x20 | 0x0788_0000 | 0x40_0000; // WUGRIDER | RIDING | DRAGON | MADOGEAR
        let option = unit::get(c, aid).map(|u| u.sc.option).unwrap_or(0);
        if !(id.nameid == ITEMID_REINS_OF_MOUNT && option & RIDE_OPTIONS != 0) {
            s.item_delay[i].tick = tick + id.delay.duration as i64;
        }
    } else {
        log::error!("pc_itemcd_add: Exceeded item delay array capacity! (nameid={}, char_id={})", id.nameid, s.char_id);
    }
    // clean up used delays so we can give room for more
    for d in s.item_delay.iter_mut() {
        if d.tick - tick <= 0 {
            d.tick = 0;
            d.nameid = 0;
        }
    }
    put_st(c, &s);
    0
}

/// pc.cpp pc_itemcd_check: an item with a delay status (`Delay.Status`) uses the status change as its cooldown.
fn pc_itemcd_check(c: &Ctx, aid: u32, id: &ItemData, tick: Tick, n: usize) -> u8 {
    // Do normal delay assignment
    if id.delay.sc <= itemdb::SC_NONE || id.delay.sc as i32 >= crate::status::consts::SC_MAX {
        return pc_itemcd_add(c, aid, id, tick, n);
    }
    // Send reply of delay remains
    if let Some(sce) = crate::status::sc::get_sce(c, aid, id.delay.sc as i32) {
        let left = if sce.timer != 0 { crate::timer::due(c, sce.timer).map(|due| (due - tick) / 1000) } else { None };
        if let Some(sd) = unit::get(c, aid) {
            crate::clif_items::msg_value(c, &sd, crate::clif_items::MSI_ITEM_REUSE_LIMIT_SECOND, left.unwrap_or(99) as i32);
        }
        return 1;
    }
    if let Some(sd) = unit::get(c, aid) {
        crate::status::sc::sc_start(c, Some(&sd), &sd, id.delay.sc as i32, 100, id.nameid as i32, id.delay.duration as i64);
    }
    0
}

// ---------------------------------------------------------------------------------------------- item scripts

/// Which of an item's scripts (item_data::script / equip_script / unequip_script).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ItemScript {
    Use,
    Equip,
    Unequip,
}

/// The key of an item script in the "item" script provider: "<nameid>", "<nameid>:equip", "<nameid>:unequip",
/// or "combo:<id>" for a combo's bonus script.
pub fn item_script_key(nameid: u32, which: ItemScript) -> String {
    match which {
        ItemScript::Use => format!("{nameid}"),
        ItemScript::Equip => format!("{nameid}:equip"),
        ItemScript::Unequip => format!("{nameid}:unequip"),
    }
}

/// The "item" script source provider (itemdb.cpp parses each script with SCRIPT_IGNORE_EXTERNAL_BRACKETS at load,
/// before any NPC file: no user functions).
pub fn item_script_source(_c: Option<&Ctx>, key: &str) -> Option<crate::script::code::ExtSource> {
    let text = if let Some(id) = key.strip_prefix("combo:") {
        let id: u16 = id.parse().ok()?;
        itemdb::combo::combo(id)?.script.clone()?
    } else {
        let (nameid, which) = match key.split_once(':') {
            Some((n, "equip")) => (n, ItemScript::Equip),
            Some((n, "unequip")) => (n, ItemScript::Unequip),
            Some(_) => return None,
            None => (key, ItemScript::Use),
        };
        let nameid: u32 = nameid.parse().ok()?;
        // setitemscript: parse_script(script, "script_setitemscript", 0, 0) (itemui/itemdb_rt.rs)
        if let Some(c) = _c {
            let n = match which {
                ItemScript::Use => 0,
                ItemScript::Equip => 1,
                ItemScript::Unequip => 2,
            };
            if let Some(rt) = crate::itemui::itemdb_rt::runtime_script(c, nameid, n) {
                return Some(crate::script::code::ExtSource { text: crate::net::wire::latin1(&rt?), file: "script_setitemscript".into(), line: 0, options: 0, userfuncs: Vec::new() });
            }
        }
        let s = itemdb::item_scripts(nameid)?;
        match which {
            ItemScript::Use => s.script.clone()?,
            ItemScript::Equip => s.equip_script.clone()?,
            ItemScript::Unequip => s.unequip_script.clone()?,
        }
    };
    Some(crate::script::code::ExtSource {
        text: crate::net::wire::latin1(&text),
        file: "db/re/item_db.yml".into(),
        line: 0,
        options: crate::script::compiler::SCRIPT_IGNORE_EXTERNAL_BRACKETS,
        userfuncs: Vec::new(),
    })
}

/// Does the item have this script (item_data::script / equip_script / unequip_script != nullptr)?
pub fn has_item_script(nameid: u32, which: ItemScript) -> bool {
    let Some(s) = itemdb::item_scripts(nameid) else { return false };
    match which {
        ItemScript::Use => s.script.is_some(),
        ItemScript::Equip => s.equip_script.is_some(),
        ItemScript::Unequip => s.unequip_script.is_some(),
    }
}

/// run_script(item->script / equip_script / unequip_script, 0, sd->id, oid).
pub fn run_item_script(c: &Ctx, aid: u32, nameid: u32, which: ItemScript, oid: u32) {
    if !has_item_script(nameid, which) {
        return;
    }
    crate::script::run_item_script(c, aid, &item_script_key(nameid, which), oid);
}

// ---------------------------------------------------------------------------------------------- using items

/// pc.cpp pc_isUseitem.
pub fn pc_isuseitem(c: &Ctx, aid: u32, n: usize) -> bool {
    let Some(r) = inv_get(c, aid, n) else { return false };
    let item = data_of(&r.item);
    let nameid = r.item.nameid;
    let Some(sd) = unit::get(c, aid) else { return false };
    let Some(ch) = status(c, aid) else { return false };
    let Some(s) = st(c, aid) else { return false };
    let Some(p) = pc::get(c, aid) else { return false };
    // Not consumable item
    if item.r#type != itemdb::IT_HEALING && item.r#type != itemdb::IT_USABLE && item.r#type != itemdb::IT_CASH {
        return false;
    }
    if pc::has_permission(c, aid, crate::rules::PC_PERM_ITEM_UNCONDITIONAL) {
        return true;
    }
    let m = sd.map;
    if crate::npc::mapflag(c, m, MF_NOITEMCONSUMPTION) {
        return false;
    }
    let tick = c.tick();
    // Prevent mass item usage. [Skotlex]
    if s.canuseitem_tick - tick > 0 || (itemdb::group::itemdb_group_item_exists(IG_CASH_FOOD, nameid) && s.canusecashfood_tick - tick > 0) {
        return false;
    }
    if item.item_usage.sitting && pc_issit(c, aid) && pc::group_level(c, aid) < item.item_usage.r#override as i32 {
        crate::clif_items::msg(c, &sd, crate::clif_items::MSI_CANT_USE_WHEN_SITDOWN);
        return false;
    }
    if p.storage_flag != 0 && item.r#type != itemdb::IT_CASH {
        // color_table[COLOR_RED]: You cannot use this item while storage is open.
        clif::messagecolor_target(c, &sd, crate::social::COLOR_RED, &crate::net::wire::latin1(crate::data::msg_txt(388)), false, Target::SelfOnly, None);
        return false;
    }
    if item.flag.dead_branch && (crate::npc::mapflag(c, m, MF_NOBRANCH) || crate::npc::mapflag_gvg2(c, m)) {
        return false;
    }
    if itemdb::group::itemdb_group_item_exists(IG_MF_NOTELEPORT, nameid) {
        if crate::npc::mapflag(c, m, crate::npc::MF_NOTELEPORT) || crate::npc::mapflag_gvg2(c, m) {
            crate::clif_items::skill_teleportmessage(c, &sd, 0); // NOTIFY_MAPINFO_CANT_TP
            return false;
        }
        // Duel: Can't use this item in duel. (duel_allow_teleport, duel.rs)
        if crate::duel::item_refused(c, &sd) {
            return false;
        }
    }
    if itemdb::group::itemdb_group_item_exists(IG_MF_NORETURN, nameid) {
        if crate::npc::mapflag(c, m, crate::npc::MF_NORETURN) {
            return false;
        }
        // Duel: Can't use this item in duel. (duel_allow_teleport, duel.rs)
        if crate::duel::item_refused(c, &sd) {
            return false;
        }
    }
    if itemdb::group::itemdb_group_item_exists(IG_GIANT_FLY_WING, nameid) {
        // pc.cpp:6340-6365: only the party leader, and only with a living member on the same map
        match crate::party::party_search(c, ch.party_id) {
            Some(pd) => {
                let leader = pd.data.iter().enumerate().any(|(i, d)| d.sd == aid && pd.party.member.get(i).map(|m| m.leader).unwrap_or(false));
                // User is not party leader
                if !leader {
                    crate::clif_items::msg(c, &sd, crate::clif_items::MSI_CANNOT_PARTYCALL);
                    return false;
                }
                let member_here = pd.data.iter().any(|d| d.sd != 0 && d.sd != aid && unit::get(c, d.sd).map(|u| u.map == sd.map).unwrap_or(false) && !pc::isdead(c, d.sd));
                // No party members found on same map
                if !member_here {
                    crate::clif_items::msg(c, &sd, crate::clif_items::MSI_NO_PARTYMEM_ON_THISMAP);
                    return false;
                }
            }
            None => {
                crate::clif_items::msg(c, &sd, crate::clif_items::MSI_CANNOT_PARTYCALL);
                return false;
            }
        }
    }
    const ITEMID_MERCENARY_RED_POTION: u32 = 12184;
    const ITEMID_MERCENARY_BLUE_POTION: u32 = 12185;
    const ITEMID_M_CENTER_POTION: u32 = 12241;
    const ITEMID_M_AWAKENING_POTION: u32 = 12242;
    const ITEMID_M_BERSERK_POTION: u32 = 12243;
    const ITEMID_NEURALIZER: u32 = 12213;
    match nameid {
        ITEMID_MERCENARY_RED_POTION | ITEMID_MERCENARY_BLUE_POTION | ITEMID_M_CENTER_POTION | ITEMID_M_AWAKENING_POTION | ITEMID_M_BERSERK_POTION => {
            // sd->md / sd->md->db (mercenary/data.rs)
            let Some(md) = crate::mercenary::data::of_master(c, aid) else { return false };
            let Some(mdb) = crate::mercenary::data::db_of(&md) else { return false };
            if crate::status::sc::cant(c, md.id, crate::status::consts::SCS_NOCONSUMEITEM) {
                return false;
            }
            if nameid == ITEMID_M_AWAKENING_POTION && mdb.lv < 40 {
                return false;
            }
            if nameid == ITEMID_M_BERSERK_POTION && mdb.lv < 80 {
                return false;
            }
        }
        ITEMID_NEURALIZER => {
            if !crate::npc::mapflag(c, m, MF_RESET) {
                return false;
            }
        }
        _ => {}
    }
    if crate::db::items::group::itemdb_group_item_exists(IG_MERCENARY, nameid) && crate::mercenary::data::of_master(c, aid).is_some() {
        return false; // Mercenary Scrolls
    }
    // Safe check type cash disappear when overweight [Napster]
    if item.flag.group || item.r#type == itemdb::IT_CASH {
        if pc_getpercentweight(&s, 0) as u32 >= open_box_weight_rate() {
            crate::clif_items::msg_color(c, &sd, crate::clif_items::MSI_PICKUP_FAILED_ITEMCREATE, crate::social::COLOR_RED);
            return false;
        }
        if pc_inventoryblank(c, aid) <= 10 {
            crate::clif_items::msg_color(c, &sd, crate::clif_items::MSI_PICKUP_FAILED_ITEMCREATE, crate::social::COLOR_RED);
            return false;
        }
    }
    // Gender check
    if item.sex != itemdb::SEX_BOTH && ch.sex != item.sex {
        return false;
    }
    // Required level check
    if item.elv != 0 && (ch.base_level as u32) < item.elv as u32 {
        return false;
    }
    if item.elvmax != 0 && (ch.base_level as u32) > item.elvmax as u32 {
        return false;
    }
    if !crate::pc_equip::pc_job_can_use_item(&ch, item) {
        return false;
    }
    if crate::status::sc::cant(c, aid, crate::status::consts::SCS_NOCONSUMEITEM) {
        return false;
    }
    if !crate::pc_equip::pc_is_item_class(&ch, item) {
        return false;
    }
    //Dead Branch items
    if item.flag.dead_branch {
        crate::map_log::log_branch(c, aid);
    }
    true
}

/// pc.cpp pc_useitem: 0 = fail, 1 = used.
pub fn pc_useitem(c: &Ctx, aid: u32, n: usize) -> i32 {
    let tick = c.tick();
    let Some(p) = pc::get(c, aid) else { return 0 };
    let Some(sd) = unit::get(c, aid) else { return 0 };
    if p.mail_writing {
        return 0;
    }
    if p.npc_id != 0 {
        if crate::pc_misc::progressbar_abort_if_running(c, aid) {
            return 0; // First item use attempt cancels the progress bar
        }
        let x = crate::script::npc_run::sdx(c, aid);
        if crate::itemui::pc_hasprogress(c, aid, crate::itemui::WIP_DISABLE_SKILLITEM) || !x.npc_item_flag {
            crate::clif_items::msg(c, &sd, crate::clif_items::MSI_BUSY); // RENEWAL
            return 0;
        }
    }
    let Some(r) = inv_get(c, aid, n) else { return 0 };
    let item = r.item.clone();
    let id = data_of(&item);
    if item.nameid == 0 || item.amount <= 0 {
        return 0;
    }
    if !pc_isuseitem(c, aid, n) {
        return 0;
    }
    let nameid = id.nameid;
    const ITEMID_NAUTHIZ: u32 = 12725;
    const OPT1_STONEWAIT: i16 = 6;
    const OPT1_BURNING: i16 = 7;
    const ITEMID_REINS_OF_MOUNT: u32 = 12622;
    if nameid != ITEMID_NAUTHIZ && sd.sc.opt1 > 0 && sd.sc.opt1 != OPT1_STONEWAIT && sd.sc.opt1 != OPT1_BURNING {
        return 0;
    }
    // Items with delayed consume are not meant to work while in mounts except reins of mount(12622)
    if id.flag.delay_consume > 0 {
        if nameid != ITEMID_REINS_OF_MOUNT && crate::status::sc::has_sce(c, aid, crate::status::consts::SC_ALL_RIDING) {
            return 0;
        } else if pc_issit(c, aid) {
            return 0;
        }
    }
    if id.flag.delay_consume > 0 && sd.ud.skilltimer != 0 {
        return 0;
    }
    if id.delay.duration > 0 && !pc::has_permission(c, aid, crate::rules::PC_PERM_ITEM_UNCONDITIONAL) && pc_itemcd_check(c, aid, id, tick, n) != 0 {
        return 0;
    }
    // on restricted maps the item is consumed but the effect is not used
    if !pc::has_permission(c, aid, crate::rules::PC_PERM_ITEM_UNCONDITIONAL) && crate::pc_equip::itemdb_is_no_equip(c, id, sd.map) {
        crate::clif_items::msg(c, &sd, crate::clif_items::MSI_IMPOSSIBLE_USEITEM_AREA);
        if allow_consume_restricted_item() && id.flag.delay_consume > 0 {
            crate::clif_items::useitemack(c, aid, n as i32, item.amount as i32 - 1, true);
            pc_delitem(c, aid, n as i32, 1, 1, 0, LOG_TYPE_CONSUME);
        }
        return 0;
    }
    if crate::db::misc::pet::pet_db_search(id.nameid as i32, crate::db::misc::pet::PetItemType::Catch).is_some() && crate::npc::mapflag(c, sd.map, MF_NOPETCAPTURE) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(669)); // You can't catch any pet on this map.
        return 0;
    }
    if let Some(mut s) = st(c, aid) {
        s.itemid = item.nameid;
        s.itemindex = n as i16;
        put_st(c, &s);
    }
    let amount = item.amount as i32;
    // Check if the item is to be consumed immediately [Skotlex]
    if id.flag.delay_consume > 0 {
        crate::clif_items::useitemack(c, aid, n as i32, amount, true);
    } else if item.expire_time == 0 && nameid != ITEMID_REINS_OF_MOUNT {
        crate::clif_items::useitemack(c, aid, n as i32, amount - 1, true);
        pc_delitem(c, aid, n as i32, 1, 1, 0, LOG_TYPE_CONSUME); // Rental Usable Items are not deleted until expiration
    } else {
        crate::clif_items::useitemack(c, aid, n as i32, 0, false);
    }
    if item.card0 == itemdb::CARD0_CREATE && crate::status::calc::pc_famerank(c, (item.card2 & 0xffff) | ((item.card3 & 0xffff) << 16), crate::db::jobs::MAPID_ALCHEMIST) != 0 {
        crate::status::hp::set_potion_flag(2); // Famous player's potions have 50% more efficiency
    }
    // Update item use time.
    if let Some(mut s) = st(c, aid) {
        s.canuseitem_tick = tick + item_use_interval();
        if itemdb::group::itemdb_group_item_exists(IG_CASH_FOOD, nameid) {
            s.canusecashfood_tick = tick + cashfood_use_interval();
        }
        put_st(c, &s);
    }
    // Save the old script the player was attached to, detach it, run the item's script, then put it back.
    let previous = crate::script::npc_run::sdx(c, aid).st;
    let mut previous_st = if previous != 0 { crate::script::state::load(c, previous) } else { None };
    if let Some(pst) = previous_st.as_mut() {
        crate::script::npc_run::script_detach_rid(c, pst);
        crate::script::state::save(c, pst);
    }
    let fake = crate::script::fake_npc_id(c);
    run_item_script(c, aid, nameid, ItemScript::Use, fake);
    let cur = crate::script::npc_run::sdx(c, aid).st;
    if cur != 0 {
        crate::script::npc_run::script_free_state_id(c, cur);
        let mut x = crate::script::npc_run::sdx(c, aid);
        x.st = 0;
        crate::script::npc_run::put_sdx(c, &x);
    }
    if previous_st.is_some() {
        if let Some(mut pst) = crate::script::state::load(c, previous) {
            pst.rid = aid;
            crate::script::npc_run::script_attach_state(c, &mut pst);
            crate::script::state::save(c, &pst);
        }
    }
    crate::status::hp::set_potion_flag(0);
    1
}

// ---------------------------------------------------------------------------------------------- pc_checkitem

/// pc.cpp pc_checkitem: equipment that is no longer allowed where it is worn is taken off (map change / login).
pub fn pc_checkitem(c: &Ctx, aid: u32) {
    let Some(p) = pc::get(c, aid) else { return };
    if p.vending {
        return;
    }
    pc_check_available_item(c, aid, ITMCHK_NONE);
    let mut calc_flag = false;
    let m = unit::get(c, aid).map(|u| u.map).unwrap_or(0);
    let all_equipment = pc::has_permission(c, aid, crate::rules::PC_PERM_USE_ALL_EQUIPMENT);
    for r in inv_list(c, aid) {
        if r.item.nameid == 0 || r.item.equip == 0 {
            continue;
        }
        let i = r.idx as i32;
        if r.item.equip & !crate::pc_equip::pc_equippoint(c, aid, &r.item) != 0 {
            crate::pc_equip::pc_unequipitem(c, aid, i, 2);
            calc_flag = true;
            continue;
        }
        if !all_equipment && !allow_equip_restricted_item() && crate::pc_equip::itemdb_is_no_equip(c, data_of(&r.item), m) {
            crate::pc_equip::pc_unequipitem(c, aid, i, 2);
            calc_flag = true;
            continue;
        }
    }
    for r in inv_list(c, aid) {
        if r.item.nameid == 0 || r.item.equip_switch == 0 {
            continue;
        }
        let bad = r.item.equip_switch & !crate::pc_equip::pc_equippoint(c, aid, &r.item) != 0
            || (!all_equipment && !allow_equip_restricted_item() && crate::pc_equip::itemdb_is_no_equip(c, data_of(&r.item), m));
        if bad {
            if let Some(mut s) = st(c, aid) {
                for e in s.equip_switch_index.iter_mut() {
                    if *e == r.idx as i16 {
                        *e = -1;
                    }
                }
                put_st(c, &s);
            }
            let mut r2 = r.clone();
            r2.item.equip_switch = 0;
            inv_put(c, &r2);
            continue;
        }
    }
    if calc_flag && pc::get(c, aid).map(|p| p.active).unwrap_or(false) {
        crate::skilltree::pc_checkallowskill(c, aid); // rA pc.cpp:12659
        crate::status::calc_pc(c, aid, crate::status::SCO_NONE);
    }
}
