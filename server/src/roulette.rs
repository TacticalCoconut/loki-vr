//! The Roulette of Fortune: clif.cpp's "Roulette System" [Yommy] (clif.cpp:20830-21134: roulette_generate_bonus,
//! clif_roulette_open, clif_parse_roulette_open / _info / _close / _generate / _item, clif_roulette_info,
//! clif_roulette_recvitem_ack, clif_roulette_getitem, clif_roulette_generate), the pc.cpp parts (pc_reg_received's
//! roulette points, the SP_ROULETTE_* parameters of pc_readparam / pc_setparam, sd->state.roulette_open), the
//! `open_roulette` script command (script.cpp:26450) and @roulette (atcommand.cpp:11320). The prize table (`rd`,
//! itemdb_parse_roulette_db) is db/items/roulette.rs.
//!
//! State: sd->roulette and sd->roulette_point are the [`PcRoulette`] row of an online character (this file owns it);
//! the points' permanent home is the character registry (`RouletteBronze`, `RouletteSilver`, `RouletteGold`:
//! pc_readreg2 / pc_setreg2, no prefix = a permanent character variable), read at pc_reg_received and written with
//! every change, as rAthena does. sd->state.roulette_open is the [`UI_ROULETTE`] bit of `pc.ui_open` (pc_cant_act2
//! tests it, pc_setpos clears it with the other window flags). sd->roulette is not saved: it goes with the session.
//!
//! The rules are written over the row, the table and the rolls (the [`Rolls`] trait: the reducer RNG, or a fixed roll
//! in the unit tests) so that they can be tested without a database; the packet handlers wrap them.

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::db::items::roulette::{rd, RouletteDb, MAX_ROULETTE_COLUMNS, MAX_ROULETTE_LEVEL};
use crate::inventory::{self as inv, Item};
use crate::net::packets::{packet_roulette_generate_ack, packet_roulette_itemrecv_ack, packet_roulette_open_ack};
use crate::net::wire::latin1;
use crate::unit;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

// ---------------------------------------------------------------------------------------------- config / constants

/// conf/battle/feature.conf:73 `feature.roulette: on` (battle_config.feature_roulette; battle.cpp:9049-9052 turns it
/// off only for PACKETVER < 20141022).
pub fn feature_roulette() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_ROULETTE) != 0
}
/// conf/battle/feature.conf:77 `feature.roulette_bonus_reward: on` (battle_config.feature_roulette_bonus_reward).
pub fn feature_roulette_bonus_reward() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_ROULETTE_BONUS_REWARD) != 0
}

/// pc.hpp:54-56 ROULETTE_BRONZE_VAR / ROULETTE_SILVER_VAR / ROULETTE_GOLD_VAR.
pub const ROULETTE_BRONZE_VAR: &str = "RouletteBronze";
pub const ROULETTE_SILVER_VAR: &str = "RouletteSilver";
pub const ROULETTE_GOLD_VAR: &str = "RouletteGold";

/// map.hpp:519-521 SP_ROULETTE_BRONZE / SP_ROULETTE_SILVER / SP_ROULETTE_GOLD (the script parameters RouletteBronze …).
pub const SP_ROULETTE_BRONZE: i64 = 128;
pub const SP_ROULETTE_SILVER: i64 = 129;
pub const SP_ROULETTE_GOLD: i64 = 130;

/// clif.hpp:106 enum RECV_ROULETTE_ITEM_REQ.
pub const RECV_ITEM_SUCCESS: u8 = 0x0;
pub const RECV_ITEM_FAILED: u8 = 0x1;
pub const RECV_ITEM_OVERCOUNT: u8 = 0x2;
pub const RECV_ITEM_OVERWEIGHT: u8 = 0x3;

/// clif.hpp:118 enum GENERATE_ROULETTE_ACK.
pub const GENERATE_ROULETTE_SUCCESS: u8 = 0x0;
pub const GENERATE_ROULETTE_FAILED: u8 = 0x1;
pub const GENERATE_ROULETTE_NO_ENOUGH_POINT: u8 = 0x2;
pub const GENERATE_ROULETTE_LOSING: u8 = 0x3;

/// packets_struct.hpp packet_headers: rouletteinfoackType / roulettgenerateackType / roulettercvitemackType, and
/// clif_roulette_open's literal 0xa1a.
const ZC_ACK_OPEN_ROULETTE: i16 = 0x0a1a;
const ZC_ACK_ROULETTE_INFO: i16 = 0x0a1c;
const ZC_ACK_GENERATE_ROULETTE: i16 = 0x0a20;
const ZC_RECV_ROULETTE_ITEM: i16 = 0x0a22;

/// `pc.ui_open` bit of sd->state.roulette_open (bits 0-6: the item windows, itemui/mod.rs; 0x1 / 0x2 are also used by
/// the barter windows, pc.rs; 8 is left to the inventory expansion window).
pub const UI_ROULETTE: u32 = 1 << 9;

/// clif.cpp color_table[COLOR_RED] (do_init_clif stores it as BGR).
const COLOR_RED: u32 = 0x0000ff;

// ---------------------------------------------------------------------------------------------- state

/// map_session_data::roulette_point and map_session_data::roulette (pc.hpp:911-922) of an online character.
#[table(accessor = pc_roulette)]
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PcRoulette {
    #[primary_key]
    pub account_id: u32,
    /// sd->roulette_point.bronze / silver / gold (the registry values as pc_reg_received read them, kept in step).
    pub bronze: i32,
    pub silver: i32,
    pub gold: i32,
    /// sd->roulette.stage (int16): the level the next turn plays on.
    pub stage: i16,
    /// sd->roulette.prizeIdx (int8): the slot the last turn stopped on, -1 for none.
    pub prize_idx: i8,
    /// sd->roulette.bonusItemID: the bonus item shown in the window (0 = none).
    pub bonus_item_id: u32,
    /// sd->roulette.prizeStage (int16): the level of the prize waiting to be claimed.
    pub prize_stage: i16,
    /// sd->roulette.claimPrize.
    pub claim_prize: bool,
    /// sd->roulette.tick: until when the wheel turns (requests before it are ignored), 0 = never turned.
    pub tick: Tick,
}

impl PcRoulette {
    /// A new map_session_data (zeroed) after pc_reg_received's `sd->roulette.prizeIdx = -1`.
    pub fn new(account_id: u32) -> Self {
        PcRoulette { account_id, bronze: 0, silver: 0, gold: 0, stage: 0, prize_idx: -1, bonus_item_id: 0, prize_stage: 0, claim_prize: false, tick: 0 }
    }
}

/// The player's roulette state (a fresh one when the session has none).
pub fn state(c: &Ctx, aid: u32) -> PcRoulette {
    c.db().pc_roulette().account_id().find(aid).unwrap_or_else(|| PcRoulette::new(aid))
}

pub fn put_state(c: &Ctx, r: &PcRoulette) {
    if c.db().pc_roulette().account_id().find(r.account_id).is_some() {
        c.db().pc_roulette().account_id().update(r.clone());
    } else {
        c.db().pc_roulette().insert(r.clone());
    }
}

/// pc.cpp pc_reg_received (pc.cpp:2385-2390): the points from the character registry (feature.roulette only), and
/// `sd->roulette.prizeIdx = -1` (the rest of sd->roulette is the zeroed map_session_data).
pub fn pc_reg_received(c: &Ctx, aid: u32) {
    let mut r = state(c, aid);
    if feature_roulette() {
        r.bronze = crate::reputation::pc_readreg2(c, aid, ROULETTE_BRONZE_VAR) as i32;
        r.silver = crate::reputation::pc_readreg2(c, aid, ROULETTE_SILVER_VAR) as i32;
        r.gold = crate::reputation::pc_readreg2(c, aid, ROULETTE_GOLD_VAR) as i32;
    }
    r.prize_idx = -1;
    put_state(c, &r);
}

/// unit_free: sd->roulette goes with the session (the registry keeps the points).
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_roulette().account_id().delete(aid);
}

/// sd->state.roulette_open = `open` (the `pc.ui_open` bit).
fn set_roulette_open(c: &Ctx, aid: u32, open: bool) {
    let Some(mut p) = crate::pc::get(c, aid) else { return };
    let v = if open { p.ui_open | UI_ROULETTE } else { p.ui_open & !UI_ROULETTE };
    if v != p.ui_open {
        p.ui_open = v;
        crate::pc::put(c, &p);
    }
}

/// sd->state.roulette_open.
pub fn roulette_open(c: &Ctx, aid: u32) -> bool {
    crate::pc::get(c, aid).map(|p| p.ui_open & UI_ROULETTE != 0).unwrap_or(false)
}

// ---------------------------------------------------------------------------------------------- parameters

/// pc_readparam's SP_ROULETTE_BRONZE / SILVER / GOLD (pc.cpp:10259-10261); None for any other parameter.
pub fn readparam(c: &Ctx, aid: u32, typ: i64) -> Option<i64> {
    if !matches!(typ, SP_ROULETTE_BRONZE | SP_ROULETTE_SILVER | SP_ROULETTE_GOLD) {
        return None;
    }
    let r = state(c, aid);
    Some(match typ {
        SP_ROULETTE_BRONZE => r.bronze,
        SP_ROULETTE_SILVER => r.silver,
        _ => r.gold,
    } as i64)
}

/// pc_setparam's SP_ROULETTE_BRONZE / SILVER / GOLD (pc.cpp:10606-10617): the value as is (no cap: pc_setreg2 keeps it
/// in the int32 range), saved in the registry, no clif_updatestatus; None for any other parameter. `val` is
/// pc_setparam's `int32 val = static_cast<uint32>(val_tmp)`.
pub fn setparam(c: &Ctx, aid: u32, typ: i64, val: i32) -> Option<bool> {
    let var = match typ {
        SP_ROULETTE_BRONZE => ROULETTE_BRONZE_VAR,
        SP_ROULETTE_SILVER => ROULETTE_SILVER_VAR,
        SP_ROULETTE_GOLD => ROULETTE_GOLD_VAR,
        _ => return None,
    };
    let mut r = state(c, aid);
    match typ {
        SP_ROULETTE_BRONZE => r.bronze = val,
        SP_ROULETTE_SILVER => r.silver = val,
        _ => r.gold = val,
    }
    put_state(c, &r);
    crate::reputation::pc_setreg2(c, aid, var, val as i64);
    Some(true)
}

// ---------------------------------------------------------------------------------------------- the rules

/// rnd() / rnd_value() (common/random.hpp): the reducer RNG, or a fixed roll in tests.
pub trait Rolls {
    fn rnd(&mut self) -> u32;
    fn rnd_value(&mut self, min: i32, max: i32) -> i32;
}

impl Rolls for &Ctx<'_> {
    fn rnd(&mut self) -> u32 {
        (**self).rnd()
    }
    fn rnd_value(&mut self, min: i32, max: i32) -> i32 {
        (**self).rnd_value(min, max)
    }
}

/// `rnd() % rd.items[level]` (rd.items is never 0 while feature.roulette is on: the load fills every level; 0 then
/// stands for rAthena's division by zero).
fn roll_idx(rolls: &mut impl Rolls, n: usize) -> usize {
    let r = rolls.rnd() as usize;
    if n == 0 {
        0
    } else {
        r % n
    }
}

/// clif.cpp roulette_generate_bonus (clif.cpp:20833): with the bonus reward on and no bonus item yet, a random slot
/// of a random level from the one the points would start on (bronze 0, else 10 silver 2, else 10 gold 4) to the last;
/// otherwise the bonus item is cleared — so opening the window again drops an unused bonus item.
pub fn roulette_generate_bonus<D: RouletteDb + ?Sized>(r: &mut PcRoulette, db: &D, bonus_reward: bool, rolls: &mut impl Rolls) {
    if bonus_reward && r.bonus_item_id == 0 {
        let mut next_stage = 0;
        if r.bronze > 0 {
            next_stage = 0;
        } else if r.silver > 9 {
            next_stage = 2;
        } else if r.gold > 9 {
            next_stage = 4;
        }
        // Get bonus item stage only from current stage or higher
        let reward_stage = rolls.rnd_value(next_stage, MAX_ROULETTE_LEVEL as i32 - 1) as usize;
        let idx = roll_idx(rolls, db.items(reward_stage));
        r.bonus_item_id = db.slot(reward_stage, idx).0;
    } else {
        r.bonus_item_id = 0;
    }
}

/// clif.cpp clif_roulette_getitem (clif.cpp:20965): hands out the prize — twice the amount when it is the bonus item
/// (and not a losing slot); a bonus item first found on a level at or below the prize's is dropped — through
/// `additem` (pc_additem with LOG_TYPE_ROULETTE: nameid, amount → e_additem_result), then a new bonus item when there
/// is none, and the turn state is reset whatever pc_additem said (an item that does not fit is lost). Returns
/// pc_additem's result, or RECV_ITEM_FAILED without a prize.
pub fn clif_roulette_getitem<D: RouletteDb + ?Sized>(
    r: &mut PcRoulette,
    db: &D,
    bonus_reward: bool,
    rolls: &mut impl Rolls,
    additem: &mut dyn FnMut(u32, i32) -> u8,
) -> u8 {
    let mut factor: i32 = 1;
    if r.prize_idx < 0 {
        return RECV_ITEM_FAILED;
    }
    let (nameid, qty, flag) = db.slot(r.prize_stage as usize, r.prize_idx as usize);
    if r.bonus_item_id != 0 {
        if r.bonus_item_id == nameid && bonus_reward && flag & 1 == 0 {
            factor = 2;
            // Reset the Bonus Item to trigger new calculation
            r.bonus_item_id = 0;
        } else {
            let mut i = 0;
            while i < MAX_ROULETTE_LEVEL && r.bonus_item_id != 0 {
                let mut j = 0;
                while j < MAX_ROULETTE_COLUMNS - i && r.bonus_item_id != 0 {
                    if db.slot(i, j).0 == r.bonus_item_id {
                        // If you reached a equal or higher level than the first level with the bonus item in it, you
                        // missed your chance and a new bonus item will be calculated
                        if r.prize_stage as i32 >= i as i32 {
                            // Reset the Bonus Item to trigger new calculation and to cancel the loops
                            r.bonus_item_id = 0;
                        }
                    }
                    j += 1;
                }
                i += 1;
            }
        }
    }
    let res = additem(nameid, qty as i32 * factor);
    if r.bonus_item_id == 0 {
        roulette_generate_bonus(r, db, bonus_reward, rolls);
    }
    r.claim_prize = false;
    r.prize_stage = 0;
    r.prize_idx = -1;
    r.stage = 0;
    res
}

/// clif.cpp clif_parse_roulette_generate after the feature check (clif.cpp:21046-21093): None while the wheel still
/// turns (the request is dropped without an answer), else the ZC_ACK_GENERATE_ROULETTE result. An unclaimed prize is
/// handed out first (which also restarts at level 0); a turn from level 0 costs 1 bronze, else 10 silver (and starts
/// on level 2), else 10 gold (level 4); the slot is rnd() % the level's slots; a losing slot (flag & 1) ends the run,
/// any other moves it one level up. `setreg` is pc_setreg2 for the point spent.
pub fn roulette_generate<D: RouletteDb + ?Sized>(
    r: &mut PcRoulette,
    db: &D,
    bonus_reward: bool,
    now: Tick,
    rolls: &mut impl Rolls,
    additem: &mut dyn FnMut(u32, i32) -> u8,
    setreg: &mut dyn FnMut(&'static str, i32),
) -> Option<u8> {
    if r.tick != 0 && r.tick - now > 0 {
        return None;
    }
    // Player has not claimed his prize yet
    if r.claim_prize {
        clif_roulette_getitem(r, db, bonus_reward, rolls, additem);
    }
    if r.stage >= MAX_ROULETTE_LEVEL as i16 {
        // Make sure everything is reset
        r.stage = 0;
        r.claim_prize = false;
        r.prize_stage = 0;
        r.prize_idx = -1;
    }
    let result;
    if r.stage == 0 && r.bronze <= 0 && r.silver < 10 && r.gold < 10 {
        result = GENERATE_ROULETTE_NO_ENOUGH_POINT;
    } else {
        if r.stage == 0 {
            if r.bronze > 0 {
                r.bronze -= 1;
                setreg(ROULETTE_BRONZE_VAR, r.bronze);
            } else if r.silver > 9 {
                r.silver -= 10;
                r.stage = 2;
                setreg(ROULETTE_SILVER_VAR, r.silver);
            } else if r.gold > 9 {
                r.gold -= 10;
                r.stage = 4;
                setreg(ROULETTE_GOLD_VAR, r.gold);
            }
        }
        r.prize_stage = r.stage;
        r.prize_idx = roll_idx(rolls, db.items(r.stage as usize)) as i8;
        r.claim_prize = true;
        r.tick = now + (MAX_ROULETTE_COLUMNS as i64 - r.prize_stage as i64 - 3).max(1) * 1000;
        if db.slot(r.stage as usize, r.prize_idx as usize).2 & 1 != 0 {
            result = GENERATE_ROULETTE_LOSING;
            r.stage = 0;
        } else {
            result = GENERATE_ROULETTE_SUCCESS;
            r.stage += 1;
        }
    }
    Some(result)
}

/// clif.cpp clif_parse_roulette_item after the feature check (clif.cpp:21109-21131): None while the wheel still turns
/// (no answer), else the ZC_RECV_ROULETTE_ITEM result — RECV_ITEM_FAILED when there is no prize to claim, otherwise
/// clif_roulette_getitem's pc_additem result mapped to the window's codes.
pub fn roulette_recv_item<D: RouletteDb + ?Sized>(
    r: &mut PcRoulette,
    db: &D,
    bonus_reward: bool,
    now: Tick,
    rolls: &mut impl Rolls,
    additem: &mut dyn FnMut(u32, i32) -> u8,
) -> Option<u8> {
    let mut ty = RECV_ITEM_FAILED;
    if r.tick != 0 && r.tick - now > 0 {
        return None;
    }
    if r.claim_prize && r.prize_idx != -1 {
        ty = match clif_roulette_getitem(r, db, bonus_reward, rolls, additem) {
            inv::ADDITEM_SUCCESS => RECV_ITEM_SUCCESS,
            inv::ADDITEM_INVALID | inv::ADDITEM_OVERITEM | inv::ADDITEM_OVERAMOUNT => RECV_ITEM_OVERCOUNT,
            inv::ADDITEM_OVERWEIGHT => RECV_ITEM_OVERWEIGHT,
            // ADDITEM_STACKLIMIT and anything else
            _ => RECV_ITEM_FAILED,
        };
    }
    Some(ty)
}

// ---------------------------------------------------------------------------------------------- packets

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

/// `clif_messagecolor(sd, color_table[COLOR_RED], msg_txt(sd, 1497), false, SELF)`: "Roulette is disabled".
fn say_disabled(c: &Ctx, aid: u32) {
    if let Some(sd) = unit::get(c, aid) {
        clif::messagecolor_target(c, &sd, COLOR_RED, &latin1(crate::data::msg_txt(1497)), false, Target::SelfOnly, None);
    }
}

/// The ZC_ACK_OPEN_ROULETTE of a state (clif_roulette_open's packet): Step is `stage - 1` and Idx the prize slot while a
/// prize waits (after a losing turn stage is 0: Step -1), else 0 and -1; the points as they are.
pub fn open_ack(r: &PcRoulette) -> packet_roulette_open_ack {
    packet_roulette_open_ack {
        PacketType: ZC_ACK_OPEN_ROULETTE,
        Result: 0, // result
        Serial: 0, // serial
        Step: if r.claim_prize { (r.stage as i32 - 1) as i8 } else { 0 },
        Idx: if r.claim_prize { r.prize_idx } else { -1 },
        AdditionItemID: r.bonus_item_id,
        GoldPoint: r.gold,
        SilverPoint: r.silver,
        BronzePoint: r.bronze,
    }
}

/// clif.cpp clif_roulette_open (clif.cpp:20855): roulette_generate_bonus, ZC_ACK_OPEN_ROULETTE (0x0a1a) and
/// sd->state.roulette_open = true. Called by CZ_REQ_OPEN_ROULETTE, @roulette and `open_roulette`.
pub fn clif_roulette_open(c: &Ctx, aid: u32) {
    let mut r = state(c, aid);
    let mut rolls = c;
    roulette_generate_bonus(&mut r, rd(), feature_roulette_bonus_reward(), &mut rolls);
    put_state(c, &r);
    let p = open_ack(&r);
    set_roulette_open(c, aid, true);
    send_self(c, aid, &p.to_vec());
}

/// packets_struct.hpp `struct packet_roulette_info_ack` for PACKETVER >= 20180511: PacketType, PacketLength,
/// RouletteSerial, then ItemInfo[42] of { uint16 Row; uint16 Position; uint32 ItemId; uint16 Count; uint16 unused; }
/// — 512 bytes, as the header probe printed (client/tools/packetgen/sizes_extra.json, 0x0a1c). It is not in
/// net/packets.rs: gen_rs.py's struct pattern (`struct X { [^{}]* }`) cannot match a struct holding a nested anonymous
/// struct, so the generator skips it; it is written here field by field instead.
pub const ROULETTE_INFO_ACK_SIZE: usize = 8 + 42 * 12;

/// clif.cpp clif_roulette_info (clif.cpp:20893): ZC_ACK_ROULETTE_INFO (0x0a1c), serial 1, every level's slots in order
/// (level, column, client_nameid, amount).
pub fn roulette_info_ack<D: RouletteDb + ?Sized>(db: &D) -> Vec<u8> {
    let mut w = Vec::with_capacity(ROULETTE_INFO_ACK_SIZE);
    w.extend_from_slice(&ZC_ACK_ROULETTE_INFO.to_le_bytes());
    w.extend_from_slice(&(ROULETTE_INFO_ACK_SIZE as i16).to_le_bytes()); // p.PacketLength = sizeof(p)
    w.extend_from_slice(&1u32.to_le_bytes()); // p.RouletteSerial = 1
    let mut count = 0;
    for i in 0..MAX_ROULETTE_LEVEL {
        for j in 0..MAX_ROULETTE_COLUMNS - i {
            let (nameid, qty, _) = db.slot(i, j);
            w.extend_from_slice(&(i as u16).to_le_bytes()); // Row
            w.extend_from_slice(&(j as u16).to_le_bytes()); // Position
            w.extend_from_slice(&crate::db::items::client_nameid(nameid).to_le_bytes()); // ItemId
            w.extend_from_slice(&qty.to_le_bytes()); // Count
            w.extend_from_slice(&0u16.to_le_bytes()); // unused
            count += 1;
        }
    }
    // 9 + 8 + … + 3 = 42 = the array; nothing is left uninitialised
    debug_assert_eq!(count, 42);
    w
}

fn clif_roulette_info(c: &Ctx, aid: u32) {
    send_self(c, aid, &roulette_info_ack(rd()));
}

/// clif.cpp clif_roulette_recvitem_ack (clif.cpp:20946): ZC_RECV_ROULETTE_ITEM (0x0a22) with the bonus item.
fn clif_roulette_recvitem_ack(c: &Ctx, aid: u32, r: &PcRoulette, ty: u8) {
    let p = packet_roulette_itemrecv_ack { PacketType: ZC_RECV_ROULETTE_ITEM, Result: ty, AdditionItemID: r.bonus_item_id };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_roulette_generate (clif.cpp:21017): ZC_ACK_GENERATE_ROULETTE (0x0a20); the bonus item only with the
/// bonus reward on.
pub fn generate_ack(r: &PcRoulette, result: u8, stage: i16, prize_idx: i16, bonus_item_id: u32) -> packet_roulette_generate_ack {
    packet_roulette_generate_ack {
        PacketType: ZC_ACK_GENERATE_ROULETTE,
        Result: result,
        Step: stage as u16,
        Idx: prize_idx as u16,
        AdditionItemID: if feature_roulette_bonus_reward() { bonus_item_id } else { 0 },
        RemainGold: r.gold,
        RemainSilver: r.silver,
        RemainBronze: r.bronze,
    }
}

// ---------------------------------------------------------------------------------------------- clif_parse_*

/// pc_additem(sd, &it, amount, LOG_TYPE_ROULETTE) of clif_roulette_getitem: `struct item it` zeroed, nameid and
/// identify = 1.
fn roulette_additem(c: &Ctx, aid: u32, nameid: u32, amount: i32) -> u8 {
    let mut it = Item::new(nameid);
    it.identify = 1;
    inv::pc_additem(c, aid, &mut it, amount, crate::map_log::LOG_TYPE_ROULETTE) // clif.cpp:21000
}

/// clif.cpp clif_parse_roulette_open (CZ_REQ_OPEN_ROULETTE 0x0a19).
pub fn parse_roulette_open(c: &Ctx, aid: u32) {
    if !feature_roulette() {
        say_disabled(c, aid); // Roulette is disabled
        return;
    }
    clif_roulette_open(c, aid);
}

/// clif.cpp clif_parse_roulette_info (CZ_REQ_ROULETTE_INFO 0x0a1b).
pub fn parse_roulette_info(c: &Ctx, aid: u32) {
    if !feature_roulette() {
        say_disabled(c, aid); // Roulette is disabled
        return;
    }
    clif_roulette_info(c, aid);
}

/// clif.cpp clif_parse_roulette_close (CZ_REQ_CLOSE_ROULETTE 0x0a1d): no answer (ZC_ACK_CLOSE_ROULETTE is never sent).
pub fn parse_roulette_close(c: &Ctx, aid: u32) {
    if !feature_roulette() {
        say_disabled(c, aid); // Roulette is disabled
        return;
    }
    set_roulette_open(c, aid, false);
}

/// clif.cpp clif_parse_roulette_generate (CZ_REQ_GENERATE_ROULETTE 0x0a1f).
pub fn parse_roulette_generate(c: &Ctx, aid: u32) {
    if !feature_roulette() {
        say_disabled(c, aid); // Roulette is disabled
        return;
    }
    let mut r = state(c, aid);
    let mut rolls = c;
    let mut additem = |nameid: u32, amount: i32| roulette_additem(c, aid, nameid, amount);
    let mut setreg = |var: &'static str, v: i32| {
        crate::reputation::pc_setreg2(c, aid, var, v as i64);
    };
    let Some(result) = roulette_generate(&mut r, rd(), feature_roulette_bonus_reward(), c.tick(), &mut rolls, &mut additem, &mut setreg) else {
        return;
    };
    put_state(c, &r);
    let idx = if r.prize_idx == -1 { 0 } else { r.prize_idx as i16 };
    let p = generate_ack(&r, result, r.prize_stage, idx, r.bonus_item_id);
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_roulette_item (CZ_RECV_ROULETTE_ITEM 0x0a21 <condition>.B; the condition is not read).
pub fn parse_roulette_item(c: &Ctx, aid: u32) {
    if !feature_roulette() {
        say_disabled(c, aid); // Roulette is disabled
        return;
    }
    let mut r = state(c, aid);
    let mut rolls = c;
    let mut additem = |nameid: u32, amount: i32| roulette_additem(c, aid, nameid, amount);
    let Some(ty) = roulette_recv_item(&mut r, rd(), feature_roulette_bonus_reward(), c.tick(), &mut rolls, &mut additem) else {
        return;
    };
    put_state(c, &r);
    clif_roulette_recvitem_ack(c, aid, &r, ty);
}

// ---------------------------------------------------------------------------------------------- @roulette / open_roulette

/// atcommand.cpp ACMD_FUNC(roulette) (PACKETVER >= 20141022, so without the msg 798 branch): the window for the
/// target (`sd`), the refusal to the invoker (`fd`). None when `func` is another command.
pub fn atcommand(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str) -> Option<i32> {
    if func != "roulette" {
        return None;
    }
    if !feature_roulette() {
        if let Some(u) = fd {
            clif::displaymessage(c, u, crate::data::msg_txt(1497)); // Roulette is disabled
        }
        return Some(-1);
    }
    clif_roulette_open(c, sd);
    Some(0)
}

/// script.cpp BUILDIN_FUNC(open_roulette) `open_roulette {<char id>};`.
fn buildin_open_roulette(rt: &crate::script::vm::Rt, st: &mut crate::script::vm::State) -> i32 {
    use crate::script::vm::{SCRIPT_CMD_FAILURE, SCRIPT_CMD_SUCCESS};
    if !feature_roulette() {
        log::error!("buildin_open_roulette: Roulette system is disabled.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(aid) = crate::script::buildins::charid2sd(rt, st, 2, "buildin_open_roulette") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    clif_roulette_open(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// The script commands of this file (script/buildins/mod.rs lookup chain).
pub fn buildin_lookup(cfunc: &str) -> Option<crate::script::buildins::BuildinFn> {
    Some(match cfunc {
        "open_roulette" => buildin_open_roulette,
        _ => return None,
    })
}

#[cfg(test)]
mod tests;
