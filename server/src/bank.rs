//! The bank: pc.cpp pc_bank_deposit / pc_bank_withdraw, the clif.cpp banking packets (0x09a6 … 0x09b9), clif_ui_open's
//! bank use (script `openbank`). The vault is the account registry `#BANKVAULT` cached in sd->bank_vault
//! (cashshop.rs [`crate::cashshop::PcPoints`]).

use crate::cashshop::{self, points, put_points, BANK_VAULT_VAR, feature_banking, MAX_BANK_ZENY};
use crate::clif::{self, Target};
use crate::inventory as inv;
use crate::net::packets::*;
use crate::net::wire::{latin1, Reader};
use crate::pc;
use crate::unit;
use crate::world::Ctx;

/// conf/battle/feature.conf `feature.banking_state_enforce: no`; conf/map_athena.conf `save_settings: 4095`
/// (CHARSAVE_BANK).
fn feature_banking_state_enforce() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_BANKING_STATE_ENFORCE) != 0
}
const CHARSAVE_BANK: bool = true;
/// script_constants MF_NOBANK.
const MF_NOBANK: i16 = 77;
/// color_table[COLOR_RED] (clif.cpp do_init_clif: 0xFF0000 stored as BGR).
const COLOR_RED: u32 = 0x0000ff;
/// clif.hpp out_ui_type OUT_UI_BANK.
pub const OUT_UI_BANK: i8 = 0;

/// clif.hpp e_BANKING_DEPOSIT_ACK.
pub const BDA_SUCCESS: i16 = 0;
pub const BDA_ERROR: i16 = 1;
pub const BDA_NO_MONEY: i16 = 2;
pub const BDA_OVERFLOW: i16 = 3;
/// clif.hpp e_BANKING_WITHDRAW_ACK.
pub const BWA_SUCCESS: i16 = 0;
pub const BWA_NO_MONEY: i16 = 1;
pub const BWA_UNKNOWN_ERROR: i16 = 2;

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

fn say_red(c: &Ctx, aid: u32, text: &str) {
    if let Some(sd) = unit::get(c, aid) {
        clif::messagecolor_target(c, &sd, COLOR_RED, &latin1(text), false, Target::SelfOnly, None);
    }
}

fn vault(c: &Ctx, aid: u32) -> i32 {
    points(c, aid).map(|p| p.bank_vault).unwrap_or(0)
}

fn zeny(c: &Ctx, aid: u32) -> i32 {
    pc::character_of(c, aid).map(|ch| ch.zeny as i32).unwrap_or(0)
}

fn set_vault(c: &Ctx, aid: u32, v: i32) {
    if let Some(mut p) = points(c, aid) {
        p.bank_vault = v;
        put_points(c, &p);
    }
    cashshop::acc_reg_set(c, aid, BANK_VAULT_VAR, v as i64);
}

/// chrif_save(sd, CSAVE_NORMAL) (chrif.rs).
fn chrif_save_normal(c: &Ctx, aid: u32) {
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_NORMAL);
}

/// pc.cpp pc_bank_deposit.
pub fn pc_bank_deposit(c: &Ctx, aid: u32, money: i32) -> i16 {
    if feature_banking_state_enforce() && !points(c, aid).map(|p| p.banking).unwrap_or(false) {
        return BDA_ERROR;
    }
    let limit_check = (money as u32).wrapping_add(vault(c, aid) as u32);
    if money <= 0 || limit_check > MAX_BANK_ZENY as u32 {
        return BDA_OVERFLOW;
    } else if money > zeny(c, aid) {
        return BDA_NO_MONEY;
    }
    if inv::pc_payzeny(c, aid, money as i64, crate::map_log::LOG_TYPE_BANK, 0) != 0 {
        return BDA_NO_MONEY;
    }
    set_vault(c, aid, vault(c, aid) + money);
    if CHARSAVE_BANK {
        chrif_save_normal(c, aid);
    }
    BDA_SUCCESS
}

/// pc.cpp pc_bank_withdraw.
pub fn pc_bank_withdraw(c: &Ctx, aid: u32, money: i32) -> i16 {
    if feature_banking_state_enforce() && !points(c, aid).map(|p| p.banking).unwrap_or(false) {
        return BWA_UNKNOWN_ERROR;
    }
    let limit_check = (money as u32).wrapping_add(zeny(c, aid) as u32);
    if money <= 0 {
        return BWA_UNKNOWN_ERROR;
    } else if money > vault(c, aid) {
        return BWA_NO_MONEY;
    } else if limit_check > inv::MAX_ZENY as u32 {
        // no official response for this scenario exists.
        say_red(c, aid, crate::data::msg_txt(1495)); // You can't withdraw that much money
        return BWA_UNKNOWN_ERROR;
    }
    if inv::pc_getzeny(c, aid, money as i64, crate::map_log::LOG_TYPE_BANK, 0) != 0 {
        return BWA_NO_MONEY;
    }
    set_vault(c, aid, vault(c, aid) - money);
    if CHARSAVE_BANK {
        chrif_save_normal(c, aid);
    }
    BWA_SUCCESS
}

// ---------------------------------------------------------------------------------------------- clif

/// clif.cpp clif_bank_open: ZC_ACK_OPEN_BANKING (0x09b7).
fn clif_bank_open(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_ACK_OPEN_BANKING { packetType: PACKET_ZC_ACK_OPEN_BANKING::ID as i16, unknown: 0 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_bank_close: ZC_ACK_CLOSE_BANKING (0x09b9).
fn clif_bank_close(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_ACK_CLOSE_BANKING { packetType: PACKET_ZC_ACK_CLOSE_BANKING::ID as i16, unknown: 0 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_Bank_Check: ZC_BANKING_CHECK (0x09a6).
fn clif_bank_check(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_BANKING_CHECK { packetType: PACKET_ZC_BANKING_CHECK::ID as i16, money: vault(c, aid) as i64, reason: 0 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_bank_deposit: ZC_ACK_BANKING_DEPOSIT (0x09a8).
fn clif_bank_deposit(c: &Ctx, aid: u32, reason: i16) {
    let p = PACKET_ZC_ACK_BANKING_DEPOSIT { packetType: PACKET_ZC_ACK_BANKING_DEPOSIT::ID as i16, reason, money: vault(c, aid) as i64, zeny: zeny(c, aid) };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_bank_withdraw: ZC_ACK_BANKING_WITHDRAW (0x09aa).
fn clif_bank_withdraw(c: &Ctx, aid: u32, reason: i16) {
    let p = PACKET_ZC_ACK_BANKING_WITHDRAW { packetType: PACKET_ZC_ACK_BANKING_WITHDRAW::ID as i16, reason, money: vault(c, aid) as i64, zeny: zeny(c, aid) };
    send_self(c, aid, &p.to_vec());
}

/// The checks every bank request starts with: banking on, not on a nobank map. True when the request may go on.
fn bank_allowed(c: &Ctx, aid: u32) -> bool {
    if !feature_banking() {
        say_red(c, aid, crate::data::msg_txt(1496)); // Banking is disabled
        return false;
    }
    let Some(sd) = unit::get(c, aid) else { return false };
    if crate::npc::mapflag(c, sd.map, MF_NOBANK) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(831)); // You cannot use the Bank on this map.
        return false;
    }
    true
}

fn packet_aid(pkt: &[u8]) -> u32 {
    Reader::at(pkt, 2).u32().unwrap_or(0)
}

/// clif.cpp clif_parse_BankOpen (CZ_REQ_OPEN_BANKING 0x09b6 <aid>.L).
pub fn parse_bank_open(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !bank_allowed(c, aid) {
        return;
    }
    if aid == packet_aid(pkt) {
        if let Some(mut p) = points(c, aid) {
            p.banking = true;
            put_points(c, &p);
        }
        clif_bank_open(c, aid);
    }
}

/// clif.cpp clif_parse_BankClose (CZ_REQ_CLOSE_BANKING 0x09b8 <aid>.L): still allowed with banking disabled.
pub fn parse_bank_close(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !feature_banking() {
        say_red(c, aid, crate::data::msg_txt(1496)); // Banking is disabled
    }
    if aid == packet_aid(pkt) {
        if let Some(mut p) = points(c, aid) {
            p.banking = false;
            put_points(c, &p);
        }
        clif_bank_close(c, aid);
    }
}

/// clif.cpp clif_parse_BankCheck (CZ_REQ_BANKING_CHECK 0x09ab <aid>.L).
pub fn parse_bank_check(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !bank_allowed(c, aid) {
        return;
    }
    if aid == packet_aid(pkt) {
        clif_bank_check(c, aid);
    }
}

/// clif.cpp clif_parse_BankDeposit (CZ_REQ_BANKING_DEPOSIT 0x09a7 <aid>.L <money>.L).
pub fn parse_bank_deposit(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_BANKING_DEPOSIT::read(&mut Reader::new(pkt)) else { return };
    if !bank_allowed(c, aid) {
        return;
    }
    if aid == p.AID {
        let reason = pc_bank_deposit(c, aid, p.zeny.max(0));
        clif_bank_deposit(c, aid, reason);
    }
}

/// clif.cpp clif_parse_BankWithdraw (CZ_REQ_BANKING_WITHDRAW 0x09a9 <aid>.L <money>.L).
pub fn parse_bank_withdraw(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_BANKING_WITHDRAW::read(&mut Reader::new(pkt)) else { return };
    if !bank_allowed(c, aid) {
        return;
    }
    if aid == p.AID {
        let reason = pc_bank_withdraw(c, aid, p.zeny.max(0));
        clif_bank_withdraw(c, aid, reason);
    }
}

/// clif.cpp clif_ui_open: the one port is itemui's (it also sets sd.state.stylist_open / enchantgrade_open).
pub use crate::itemui::clif_ui_open;
