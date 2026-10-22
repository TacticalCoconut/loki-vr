//! map/mail.cpp + the RoDEX parts of clif.cpp and intif.cpp (PACKETVER 20211103: the RODEX system of
//! PACKETVER >= 20150513 / >= 20170419; the old 0x0240-0x0274 mail window only answers @mail).
//!
//! sd->mail (the attachments being written, the cached inbox, the pending amounts) is the [`PcMail`] row of an
//! online character. The char server's half is int_mail.rs; requests go through intif.rs's queue and their answers
//! arrive after the function that asked (int_mail.rs [`MailInter`]).

use spacetimedb::{table, SpacetimeType, Table};

use crate::clif::{self, Target};
use crate::int_mail::{self, MailData, MailInter, MailMessage, *};
use crate::intif::{self, Inter};
use crate::inventory::{self as inv, Item, MAX_INVENTORY, MAX_ZENY};
use crate::net::packets::*;
use crate::net::wire::{from_latin1, latin1, until_nul, Reader};
use crate::pc::{self, pc as _};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// conf/battle/misc.conf: `mail_show_status: 0`, `mail_daily_count: 100`, `mail_zeny_fee: 2`,
/// `mail_attachment_price: 2500`, `mail_attachment_weight: 2000`, `mail_delay: 1000`.
pub fn mail_show_status() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAIL_SHOW_STATUS)
}
pub fn mail_daily_count() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAIL_DAILY_COUNT)
}
pub fn mail_zeny_fee() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAIL_ZENY_FEE)
}
pub fn mail_attachment_price() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAIL_ATTACHMENT_PRICE)
}
pub fn mail_attachment_weight() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAIL_ATTACHMENT_WEIGHT)
}
pub fn mail_delay() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MAIL_DELAY) as i64
}
/// conf/map_athena.conf `save_settings: 4095` (CHARSAVE_MAIL 0x010).
const CHARSAVE_MAIL: bool = true;

/// mail.hpp enum mail_attach_result (PACKETVER >= 20150513).
pub const MAIL_ATTACH_SUCCESS: u8 = 0;
pub const MAIL_ATTACH_WEIGHT: u8 = 1;
pub const MAIL_ATTACH_ERROR: u8 = 2;
pub const MAIL_ATTACH_SPACE: u8 = 3;
pub const MAIL_ATTACH_UNTRADEABLE: u8 = 4;
pub const MAIL_ATTACH_EQUIPSWITCH: u8 = 99;

/// clif.hpp enum mail_send_result.
pub const WRITE_MAIL_SUCCESS: i8 = 0;
pub const WRITE_MAIL_FAILED: i8 = 1;
pub const WRITE_MAIL_FAILED_CNT: i8 = 2;

/// clif.cpp enum mail_type.
const MAIL_TYPE_TEXT: u8 = 0x0;
const MAIL_TYPE_ZENY: u8 = 0x2;
const MAIL_TYPE_ITEM: u8 = 0x4;
const MAIL_TYPE_NPC: u8 = 0x8;

/// packets_struct.hpp rodex* packet ids.
const RODEXICON: u16 = 0x09e7;
const RODEXWRITERESULT: u16 = 0x09ed;
const RODEXGETZENY: u16 = 0x09f2;
const RODEXGETITEM: u16 = 0x09f4;
const RODEXDELETE: u16 = 0x09f6;
const RODEXREMOVEITEM: u16 = 0x0a07;
const RODEXOPENWRITE: u16 = 0x0a12;
const RODEXMAILLIST: u16 = 0x0ac2;

/// clif.hpp MSI_SWAP_EQUIPITEM_UNREGISTER_FIRST.
const MSI_SWAP_EQUIPITEM_UNREGISTER_FIRST: u16 = 3015;
/// script_constants MF_NORODEX.
const MF_NORODEX: i16 = 71;

/// One of sd->mail.item[MAIL_MAX_ITEM] (the attachment being written: inventory index, nameid, amount).
#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct MailItemRef {
    pub nameid: u32,
    pub index: i32,
    pub amount: i32,
}

/// map_session_data::s_mail (+ cansendmail_tick) of an online character.
#[table(accessor = pc_mail)]
#[derive(Clone, Debug)]
pub struct PcMail {
    #[primary_key]
    pub account_id: u32,
    pub char_id: u32,
    pub item: Vec<MailItemRef>,
    pub zeny: i32,
    pub inbox: MailData,
    /// if true, should sync with charserver on next mailbox request
    pub changed: bool,
    pub pending_weight: u32,
    pub pending_zeny: u32,
    pub pending_slots: u16,
    pub dest_id: u32,
    /// sd->cansendmail_tick [Mail System Flood Protection].
    pub cansendmail_tick: Tick,
}

fn pm(c: &Ctx, aid: u32) -> Option<PcMail> {
    c.db().pc_mail().account_id().find(aid)
}

fn put_pm(c: &Ctx, m: &PcMail) {
    c.db().pc_mail().account_id().update(m.clone());
}

fn sd_of(c: &Ctx, aid: u32) -> Option<Unit> {
    unit::get(c, aid)
}

fn send_self(c: &Ctx, aid: u32, b: &[u8]) {
    if let Some(sd) = sd_of(c, aid) {
        clif::send(c, b, &sd, Target::SelfOnly);
    }
}

fn to_char(m: MailInter) {
    intif::send(Inter::Mail(m));
}

/// map_charid2sd → the account id.
fn charid2aid(c: &Ctx, char_id: u32) -> Option<u32> {
    c.db().pc().char_id().find(char_id).map(|p| p.account_id)
}

// ---------------------------------------------------------------------------------------------- session

/// pc_setnewpc's `sd->cansendmail_tick = tick` and `sd->mail.pending_* = 0`, then pc_reg_received's
/// `intif_Mail_requestinbox(char_id, 0, MAIL_INBOX_NORMAL)` answered at once (the char server's answer is the next
/// thing on its socket after the bonus scripts, before the quest log: see pc.rs).
pub fn pc_login(c: &Ctx, aid: u32) {
    let Some(p) = pc::get(c, aid) else { return };
    let row = PcMail {
        account_id: aid,
        char_id: p.char_id,
        item: vec![MailItemRef::default(); MAIL_MAX_ITEM],
        zeny: 0,
        inbox: MailData::default(),
        changed: false,
        pending_weight: 0,
        pending_zeny: 0,
        pending_slots: 0,
        dest_id: 0,
        cansendmail_tick: c.tick(),
    };
    if pm(c, aid).is_some() {
        put_pm(c, &row);
    } else {
        c.db().pc_mail().insert(row);
    }
    int_mail::ensure_timers(c);
    // intif_Mail_requestinbox → mapif_Mail_sendinbox → intif_parse_Mail_inboxreceived
    let md = int_mail::mail_fromsql(c, p.char_id);
    intif_parse_mail_inboxreceived(c, p.char_id, 0, MAIL_INBOX_NORMAL, md);
}

/// unit_free: the mail state goes with the session.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_mail().account_id().delete(aid);
}

/// clif_parse_LoadEndAck's end: `if( sd->mail.inbox.unread ) clif_Mail_new(sd, 0, nullptr, nullptr)`.
pub fn load_end_ack(c: &Ctx, aid: u32) {
    if pm(c, aid).map(|m| m.inbox.unread != 0).unwrap_or(false) {
        clif_mail_new(c, aid);
    }
}

// ---------------------------------------------------------------------------------------------- mail.cpp

/// mail.cpp mail_clear.
pub fn mail_clear(c: &Ctx, aid: u32) {
    let Some(mut m) = pm(c, aid) else { return };
    for it in m.item.iter_mut() {
        *it = MailItemRef::default();
    }
    m.zeny = 0;
    m.dest_id = 0;
    put_pm(c, &m);
}

/// mail.cpp mail_removeitem: `idx` is the client's index. flag 1: the item leaves the inventory for good (the
/// attachment price is paid); flag 0: it goes back from the mail being written (ZC_ACK_REMOVE_ITEM_MAIL).
pub fn mail_removeitem(c: &Ctx, aid: u32, flag: bool, idx: i32, amount: i32) -> bool {
    let idx = idx - 2;
    if idx < 0 || idx >= MAX_INVENTORY as i32 {
        return false;
    }
    let have = inv::inv_get(c, aid, idx as usize).map(|r| r.item.amount as i32).unwrap_or(0);
    if amount <= 0 || amount > have {
        return false;
    }
    let Some(mut m) = pm(c, aid) else { return false };
    let Some(mut i) = m.item.iter().position(|e| e.index == idx && e.nameid > 0) else { return false };
    if flag {
        if mail_attachment_price() > 0 && inv::pc_payzeny(c, aid, mail_attachment_price() as i64, crate::map_log::LOG_TYPE_MAIL, 0) != 0 {
            return false;
        }
        // RODEX refreshes the client inventory from the ACK packet
        inv::pc_delitem(c, aid, idx, amount, 0, 0, crate::map_log::LOG_TYPE_MAIL);
    } else {
        m.item[i].amount -= amount;
        // Item was removed completely
        if m.item[i].amount <= 0 {
            // Move the rest of the array forward
            while i < MAIL_MAX_ITEM - 1 {
                if m.item[i + 1].nameid == 0 {
                    break;
                }
                m.item[i] = m.item[i + 1];
                i += 1;
            }
            // Zero the rest
            for e in m.item.iter_mut().skip(i) {
                *e = MailItemRef::default();
            }
        }
        put_pm(c, &m);
        clif_mail_removeitem(c, aid, true, idx + 2, amount);
    }
    true
}

/// mail.cpp mail_removezeny: flag true pays the zeny (with mail_zeny_fee %), false only refreshes the client.
pub fn mail_removezeny(c: &Ctx, aid: u32, flag: bool) -> bool {
    let Some(m) = pm(c, aid) else { return false };
    if m.zeny > 0 {
        // Zeny send
        if flag {
            let mut zeny = m.zeny as i64;
            if mail_zeny_fee() > 0 {
                let Some(fee) = zeny.checked_mul(mail_zeny_fee() as i64) else { return false };
                if fee < 0 {
                    return false;
                }
                let fee = fee / 100;
                if fee > MAX_ZENY {
                    return false;
                }
                let Some(z) = zeny.checked_add(fee) else { return false };
                zeny = z;
                if zeny > MAX_ZENY {
                    return false;
                }
            }
            // It's possible that we don't know what the dest_id is, so it will be 0
            if inv::pc_payzeny(c, aid, zeny as i32 as i64, crate::map_log::LOG_TYPE_MAIL, m.dest_id) != 0 {
                return false;
            }
        } else {
            // Update is called by pc_payzeny, so only call it in the else condition
            inv::updatestatus(c, aid, inv::SP_ZENY);
        }
    }
    if let Some(mut m) = pm(c, aid) {
        m.zeny = 0;
        put_pm(c, &m);
    }
    true
}

/// mail.cpp mail_setitem: zeny (idx 0) or an inventory item (the client's index) goes into the mail being written.
pub fn mail_setitem(c: &Ctx, aid: u32, idx: i32, amount: u32) -> u8 {
    let Some(p) = pc::get(c, aid) else { return MAIL_ATTACH_ERROR };
    if inv::pc_istrading(&p) {
        return MAIL_ATTACH_ERROR;
    }
    let Some(ch) = pc::character_of(c, aid) else { return MAIL_ATTACH_ERROR };
    let Some(mut m) = pm(c, aid) else { return MAIL_ATTACH_ERROR };
    if idx == 0 {
        // Zeny Transfer
        if !inv::pc_can_give_items(c, aid) {
            return MAIL_ATTACH_UNTRADEABLE;
        }
        // ( amount + battle_config.mail_zeny_fee / 100 * amount ) > sd->status.zeny: unsigned, and 2 / 100 is 0
        let fee_part = ((mail_zeny_fee() / 100) as u32).wrapping_mul(amount);
        if amount.wrapping_add(fee_part) > ch.zeny {
            return MAIL_ATTACH_ERROR;
        }
        m.zeny = amount as i32;
        put_pm(c, &m);
        return MAIL_ATTACH_SUCCESS;
    }
    // Item Transfer
    let idx = idx - 2;
    if idx < 0 || idx >= MAX_INVENTORY as i32 {
        return MAIL_ATTACH_ERROR;
    }
    let Some(r) = inv::inv_get(c, aid, idx as usize) else { return MAIL_ATTACH_ERROR };
    let it = r.item.clone();
    if crate::db::items::itemdb_ishatched_egg(it.card0, it.attribute) {
        return MAIL_ATTACH_ERROR;
    }
    if it.equip_switch != 0 {
        return MAIL_ATTACH_EQUIPSWITCH;
    }
    let weight_of = |index: i32| -> Option<i32> { inv::inv_get(c, aid, index.max(0) as usize).filter(|_| index >= 0).map(|r| inv::data_of(&r.item).weight as i32) };
    let my_weight = inv::data_of(&it).weight as i32;
    let i;
    if let Some(k) = m.item.iter().position(|e| e.index == idx && e.nameid > 0) {
        // The same item had already been added to the mail
        if !crate::db::items::itemdb_isstackable(m.item[k].nameid) {
            return MAIL_ATTACH_ERROR;
        }
        // Check if it exceeds the total amount
        if (amount as i64 + m.item[k].amount as i64) > it.amount as i64 {
            return MAIL_ATTACH_ERROR;
        }
        // Check if it exceeds the total weight
        if mail_attachment_weight() != 0 {
            let mut total: i32 = 0;
            for e in m.item.iter() {
                if e.nameid == 0 {
                    continue;
                }
                let Some(w) = weight_of(e.index) else { return MAIL_ATTACH_ERROR };
                total = total.wrapping_add(e.amount.wrapping_mul(w / 10));
            }
            // Add the newly added weight to the current total
            total = total.wrapping_add((amount as i32).wrapping_mul(my_weight) / 10);
            if total > mail_attachment_weight() {
                return MAIL_ATTACH_WEIGHT;
            }
        }
        m.item[k].amount += amount as i32;
        put_pm(c, &m);
        return MAIL_ATTACH_SUCCESS;
    } else {
        let Some(k) = m.item.iter().position(|e| e.nameid == 0) else { return MAIL_ATTACH_SPACE };
        // Check if it exceeds the total weight
        if mail_attachment_weight() != 0 {
            let mut total: i32 = 0;
            // Only need to sum up all entries until the new entry
            for e in m.item.iter().take(k) {
                let Some(w) = weight_of(e.index) else { return MAIL_ATTACH_ERROR };
                total = total.wrapping_add(e.amount.wrapping_mul(w / 10));
            }
            // Add the newly added weight to the current total
            total = total.wrapping_add((amount as i32).wrapping_mul(my_weight) / 10);
            if total > mail_attachment_weight() {
                return MAIL_ATTACH_WEIGHT;
            }
        }
        i = k;
    }
    if amount as i64 > it.amount as i64 {
        return MAIL_ATTACH_ERROR;
    }
    let gmlv = pc::group_level(c, aid);
    if !inv::pc_can_give_items(c, aid)
        || it.expire_time != 0
        || !crate::db::items::itemdb_available(it.nameid)
        || !crate::db::items::itemdb_isrestricted(it.nameid, &it.cards(), gmlv, 0, crate::db::items::itemdb_canmail_sub)
        || (it.bound != 0 && !inv::pc_can_give_bounded_items(c, aid))
    {
        return MAIL_ATTACH_UNTRADEABLE;
    }
    m.item[i] = MailItemRef { index: idx, nameid: it.nameid, amount: amount as i32 };
    put_pm(c, &m);
    MAIL_ATTACH_SUCCESS
}

/// mail.cpp mail_setattachment: the attachments are copied into the message and taken from the sender (the
/// attachment price and the zeny with its fee are paid).
fn mail_setattachment(c: &Ctx, aid: u32, msg: &mut MailMessage) -> bool {
    let Some(m) = pm(c, aid) else { return false };
    let Some(ch) = pc::character_of(c, aid) else { return false };
    let Some(s) = inv::st(c, aid) else { return false };
    let mut amount: i32 = 0;
    for i in 0..MAIL_MAX_ITEM {
        let e = m.item[i];
        if e.nameid == 0 || e.amount == 0 {
            msg.item[i] = Item::new(0);
            continue;
        }
        amount += 1;
        let slot = if e.index >= 0 { inv::inv_get(c, aid, e.index as usize) } else { None };
        let (nameid, have) = slot.as_ref().map(|r| (r.item.nameid, r.item.amount as i32)).unwrap_or((0, 0));
        if nameid != e.nameid {
            return false;
        }
        if have < e.amount {
            return false;
        }
        if s.weight > s.max_weight {
            // TODO (rAthena): Why check something weird like this here?
            return false;
        }
        let mut it = slot.map(|r| r.item).unwrap_or_else(|| Item::new(0));
        it.amount = e.amount as i16;
        msg.item[i] = it;
    }
    let need = m.zeny.wrapping_add(m.zeny.wrapping_mul(mail_zeny_fee()) / 100).wrapping_add(amount.wrapping_mul(mail_attachment_price()));
    if m.zeny < 0 || need > ch.zeny as i32 {
        return false;
    }
    msg.zeny = m.zeny as u32;
    // Removes the attachment from sender
    for e in m.item.iter() {
        if e.nameid == 0 || e.amount == 0 {
            // Exit the loop on the first empty entry
            break;
        }
        mail_removeitem(c, aid, true, e.index + 2, e.amount);
    }
    mail_removezeny(c, aid, true);
    true
}

/// mail.cpp mail_getattachment: the zeny / items the char server released arrive in the inventory.
fn mail_getattachment(c: &Ctx, aid: u32, slot: usize, zeny: i32, mut item: Vec<Item>) {
    let mut item_received = false;
    for i in 0..MAIL_MAX_ITEM.min(item.len()) {
        if item[i].nameid > 0 && item[i].amount > 0 {
            // Item does not exist (anymore?)
            let Some(id) = crate::db::items::itemdb_exists(item[i].nameid) else { continue };
            // Reduce the pending weight
            if let Some(mut m) = pm(c, aid) {
                m.pending_weight = m.pending_weight.wrapping_sub(id.weight.wrapping_mul(item[i].amount as u32));
                put_pm(c, &m);
            }
            // If it is a pet egg and the card data does not contain a pet id or other special ids are set
            if crate::db::misc::pet::pet_db_search(item[i].nameid as i32, crate::db::misc::pet::PetItemType::Egg).is_some() && item[i].card0 == 0 {
                // Create a new pet
                if crate::pet::pet_create_egg(c, aid, item[i].nameid) {
                    if let Some(mut m) = pm(c, aid) {
                        m.pending_slots = m.pending_slots.wrapping_sub(1);
                        put_pm(c, &m);
                    }
                    item_received = true;
                } else {
                    // Do not send receive packet so that the mail is still displayed with item attachment
                    item_received = false;
                    // Additionally stop the processing
                    break;
                }
            } else {
                let check = inv::pc_checkadditem(c, aid, item[i].nameid, item[i].amount as i32);
                // Add the item normally
                let amt = item[i].amount as i32;
                if check != inv::CHKADDITEM_OVERAMOUNT && inv::pc_additem(c, aid, &mut item[i], amt, crate::map_log::LOG_TYPE_MAIL) == inv::ADDITEM_SUCCESS {
                    item_received = true;
                    // Only reduce slots if it really required a new slot
                    if check == inv::CHKADDITEM_NEW {
                        if let Some(mut m) = pm(c, aid) {
                            m.pending_slots = m.pending_slots.wrapping_sub(id.inventory_slot_needed(amt) as u16);
                            put_pm(c, &m);
                        }
                    }
                } else {
                    // Do not send receive packet so that the mail is still displayed with item attachment
                    item_received = false;
                    // Additionally stop the processing
                    break;
                }
            }
            // Make sure no requests are possible anymore
            item[i].amount = 0;
        }
    }
    if item_received {
        clif_mail_getattachment(c, aid, slot, 0, MAIL_ATT_ITEM);
    }
    // Zeny receive
    if zeny > 0 {
        // Reduce the pending zeny
        if let Some(mut m) = pm(c, aid) {
            m.pending_zeny = m.pending_zeny.wrapping_sub(zeny as u32);
            put_pm(c, &m);
        }
        // Add the zeny (log_charid: msg->send_id)
        let send_id = pm(c, aid).and_then(|m| m.inbox.msg.get(slot).map(|msg| msg.send_id)).unwrap_or(0);
        inv::pc_getzeny(c, aid, zeny as i64, crate::map_log::LOG_TYPE_MAIL, send_id);
        clif_mail_getattachment(c, aid, slot, 0, MAIL_ATT_ZENY);
    }
}

/// mail.cpp mail_openmail (@mail): the pre-RODEX mail window (ZC_MAIL_WINDOWS 0x0260 open).
pub fn mail_openmail(c: &Ctx, aid: u32) -> i32 {
    let Some(p) = pc::get(c, aid) else { return 0 };
    if p.storage_flag != 0 || p.vending || p.buyingstore || p.trading {
        return 0;
    }
    clif_mail_window(c, aid, 0);
    1
}

/// mail.cpp mail_deliveryfail: the attachments come back (the attachment price only when zeny was attached too).
fn mail_deliveryfail(c: &Ctx, aid: u32, msg: &MailMessage) {
    let mut zeny: i32 = 0;
    let mut items = msg.item.clone();
    for it in items.iter_mut().take(MAIL_MAX_ITEM) {
        if it.amount > 0 {
            // Item receive (due to failure)
            let amt = it.amount as i32;
            inv::pc_additem(c, aid, it, amt, crate::map_log::LOG_TYPE_MAIL);
            zeny += mail_attachment_price();
        }
    }
    if msg.zeny > 0 {
        let z = msg.zeny as i32;
        inv::pc_getzeny(c, aid, z.wrapping_add(z.wrapping_mul(mail_zeny_fee()) / 100).wrapping_add(zeny) as i64, crate::map_log::LOG_TYPE_MAIL, 0); // Zeny receive (due to failure)
    }
    clif_mail_send(c, aid, WRITE_MAIL_FAILED);
}

/// mail.cpp mail_invalid_operation (PACKETVER >= 20150513): RODEX is refused on maps with the norodex flag.
pub fn mail_invalid_operation(c: &Ctx, aid: u32) -> bool {
    let Some(sd) = sd_of(c, aid) else { return true };
    if crate::npc::mapflag(c, sd.map, MF_NORODEX) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(796)); // You cannot use RODEX on this map.
        return true;
    }
    false
}

/// mail.cpp mail_send.
fn mail_send(c: &Ctx, aid: u32, dest_name: &str, title: &str, body_msg: &str, body_len: i32) {
    let Some(p) = pc::get(c, aid) else { return };
    if p.trading {
        return;
    }
    let Some(sd) = sd_of(c, aid) else { return };
    let Some(m) = pm(c, aid) else { return };
    if m.cansendmail_tick - c.tick() > 0 {
        clif::displaymessage(c, &sd, crate::data::msg_txt(675)); // "Cannot send mails too fast!!."
        clif_mail_send(c, aid, WRITE_MAIL_FAILED); // fail
        return;
    }
    if mail_daily_count() != 0 {
        mail_refresh_remaining_amount(c, aid);
        use crate::status::consts::SC_DAILYSENDMAILCNT;
        // After calling mail_refresh_remaining_amount the status should always be there
        match crate::status::sc::get_sce(c, aid, SC_DAILYSENDMAILCNT) {
            Some(e) if e.val2 < mail_daily_count() => {
                if let Some(u) = sd_of(c, aid) {
                    crate::status::sc::sc_start2(c, Some(&u), &u, SC_DAILYSENDMAILCNT, 100, dayofyear(c), e.val2 + 1, crate::status::sc::INFINITE_TICK);
                }
            }
            _ => {
                clif_mail_send(c, aid, WRITE_MAIL_FAILED_CNT);
                return;
            }
        }
    }
    let body_len = body_len.min(MAIL_BODY_LENGTH as i32);
    // (struct mail_message msg is not initialised in rAthena; every field the char server reads is set below)
    let mut msg = MailMessage::new();
    if !mail_setattachment(c, aid, &mut msg) {
        // Invalid Append condition
        clif_mail_send(c, aid, WRITE_MAIL_FAILED); // fail
        for i in 0..MAIL_MAX_ITEM {
            // (the array shifts while it is emptied, as in rAthena)
            let e = pm(c, aid).map(|m| m.item[i]).unwrap_or_default();
            mail_removeitem(c, aid, false, e.index + 2, e.amount);
        }
        mail_removezeny(c, aid, false);
        return;
    }
    let Some(ch) = pc::character_of(c, aid) else { return };
    msg.id = 0; // id will be assigned by charserver
    msg.send_id = ch.id;
    msg.dest_id = 0; // will attempt to resolve name
    msg.send_name = int_mail::strncpy(&ch.name, NAME_LENGTH);
    msg.dest_name = int_mail::strncpy(dest_name, NAME_LENGTH);
    msg.title = int_mail::strncpy(title, MAIL_TITLE_LENGTH);
    msg.kind = MAIL_INBOX_NORMAL;
    if msg.title.is_empty() {
        return; // Message has no length and somehow client verification was skipped.
    }
    msg.body = if body_len != 0 { int_mail::strncpy(body_msg, ((body_len + 1) as usize).min(MAIL_BODY_LENGTH)) } else { String::new() };
    msg.timestamp = c.tick() / 1000;
    intif_mail_send(c, ch.account_id, msg);
    if let Some(mut m) = pm(c, aid) {
        m.cansendmail_tick = c.tick() + mail_delay(); // Flood Protection
        put_pm(c, &m);
    }
}

/// date.cpp date_get_dayofyear (localtime's tm_yday: the oracle's time zone, script/npc_run.rs).
fn dayofyear(c: &Ctx) -> i32 {
    crate::script::npc_run::localtime(c.tick().div_euclid(1000)).yday
}

/// mail.cpp mail_refresh_remaining_amount: the daily send counter (SC_DAILYSENDMAILCNT val1 = day of the year, val2
/// = mails sent) starts again on a new day.
pub fn mail_refresh_remaining_amount(c: &Ctx, aid: u32) {
    use crate::status::consts::SC_DAILYSENDMAILCNT;
    let doy = dayofyear(c);
    // If it was not yet started or it was started on another day
    let restart = crate::status::sc::get_sce(c, aid, SC_DAILYSENDMAILCNT).map(|e| e.val1 != doy).unwrap_or(true);
    if restart {
        if let Some(u) = sd_of(c, aid) {
            crate::status::sc::sc_start2(c, Some(&u), &u, SC_DAILYSENDMAILCNT, 100, doy, 0, crate::status::sc::INFINITE_TICK);
        }
    }
}

// ---------------------------------------------------------------------------------------------- intif.cpp

/// intif.cpp intif_Mail_send (0x304d): a mail for the char server to deliver. Called by mail_send and by
/// buildin_mail (account id 0), and by pc_attendance_claim_reward (extras agent). Returns false only when the char
/// server is not connected (never in the module).
pub fn intif_mail_send(c: &Ctx, account_id: u32, msg: MailMessage) -> bool {
    int_mail::ensure_timers(c);
    to_char(MailInter::Send { account_id, msg });
    true
}

/// intif.cpp intif_parse_Mail_inboxreceived (0x3848).
pub fn intif_parse_mail_inboxreceived(c: &Ctx, char_id: u32, flag: u8, kind: u8, md: MailData) {
    let Some(aid) = charid2aid(c, char_id) else {
        log::error!("intif_parse_Mail_inboxreceived: char not found {char_id}");
        return;
    };
    let Some(mut m) = pm(c, aid) else { return };
    m.inbox = md;
    m.changed = false; // cache is now in sync
    put_pm(c, &m);
    // Refresh top right icon
    clif_mail_new(c, aid);
    if flag != 0 {
        clif_mail_refreshinbox(c, aid, kind, 0);
    } else if mail_show_status() != 0 && (mail_show_status() == 1 || m.inbox.unread != 0) {
        if let Some(sd) = sd_of(c, aid) {
            let out = crate::data::format_msg(crate::data::msg_txt(510), &[&m.inbox.unchecked, &(m.inbox.unread + m.inbox.unchecked)]);
            // color_table[COLOR_LIGHT_GREEN] (0xB5FFB5 as BGR)
            clif::messagecolor_target(c, &sd, 0xb5ffb5, latin1(&out).as_slice(), false, Target::SelfOnly, None);
        }
    }
}

/// intif.cpp intif_parse_Mail_getattach (0x384a).
pub fn intif_parse_mail_getattach(c: &Ctx, char_id: u32, mail_id: i32, zeny: i32, items: Vec<Item>) {
    let Some(aid) = charid2aid(c, char_id) else {
        log::error!("intif_parse_Mail_getattach: char not found {char_id}");
        return;
    };
    let Some(m) = pm(c, aid) else { return };
    let Some(i) = m.inbox.msg.iter().position(|x| x.id == mail_id) else { return };
    mail_getattachment(c, aid, i, zeny, items);
}

/// intif.cpp intif_parse_Mail_delete (0x384b).
pub fn intif_parse_mail_delete(c: &Ctx, char_id: u32, mail_id: i32, failed: bool) {
    let Some(aid) = charid2aid(c, char_id) else {
        log::error!("intif_parse_Mail_delete: char not found {char_id}");
        return;
    };
    if failed {
        return;
    }
    let Some(mut m) = pm(c, aid) else { return };
    let Some(i) = m.inbox.msg.iter().position(|x| x.id == mail_id) else { return };
    let kind = m.inbox.msg[i].kind;
    clif_mail_delete(c, aid, &m.inbox.msg[i], true);
    m.inbox.msg[i] = MailMessage::new();
    m.inbox.amount -= 1;
    put_pm(c, &m);
    if m.inbox.full || m.inbox.unchecked > 0 {
        intif_mail_requestinbox(char_id, 1, kind); // Free space is available for new mails
    }
}

/// intif.cpp intif_parse_Mail_return (0x384c).
pub fn intif_parse_mail_return(c: &Ctx, char_id: u32, mail_id: i32, fail: bool) {
    let Some(aid) = charid2aid(c, char_id) else {
        log::error!("intif_parse_Mail_return: char not found {char_id}");
        return;
    };
    if !fail {
        if let Some(mut m) = pm(c, aid) {
            if let Some(i) = m.inbox.msg.iter().position(|x| x.id == mail_id) {
                let kind = m.inbox.msg[i].kind;
                m.inbox.msg[i] = MailMessage::new();
                m.inbox.amount -= 1;
                put_pm(c, &m);
                if m.inbox.full {
                    intif_mail_requestinbox(char_id, 1, kind); // Free space is available for new mails
                }
            }
        }
    }
    clif_mail_return(c, aid, mail_id, fail as i16);
}

/// intif.cpp intif_parse_Mail_send (0x384d): the sender learns whether the mail went out.
pub fn intif_parse_mail_send(c: &Ctx, msg: MailMessage) {
    let fail = msg.id == 0;
    // notify sender
    let Some(aid) = charid2aid(c, msg.send_id) else { return };
    if fail {
        mail_deliveryfail(c, aid, &msg);
    } else {
        clif_mail_send(c, aid, WRITE_MAIL_SUCCESS);
        if CHARSAVE_MAIL {
            chrif_save_inventory(c, aid);
        }
    }
}

/// chrif_save(sd, CSAVE_INVENTORY) (chrif.rs).
fn chrif_save_inventory(c: &Ctx, aid: u32) {
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_INVENTORY);
}

/// intif.cpp intif_parse_Mail_new (0x3849): a new mail for an online character.
pub fn intif_parse_mail_new(c: &Ctx, dest_id: u32, _mail_id: i32, _sender_name: &str, _title: &str, kind: u8) {
    let Some(aid) = charid2aid(c, dest_id) else { return };
    let Some(mut m) = pm(c, aid) else { return };
    m.changed = true;
    m.inbox.unread += 1;
    put_pm(c, &m);
    clif_mail_new(c, aid);
    // Make sure the window gets refreshed when its open
    intif_mail_requestinbox(dest_id, 1, kind);
}

/// intif.cpp intif_parse_Mail_receiver (0x384e).
pub fn intif_parse_mail_receiver(c: &Ctx, requesting_char_id: u32, char_id: u32, class: u16, base_level: u16, name: &str) {
    // Only f the player is online
    let Some(aid) = charid2aid(c, requesting_char_id) else { return };
    clif_mail_receiver_ack(c, aid, char_id, class as i16, base_level as u32, name);
    if let Some(mut m) = pm(c, aid) {
        m.dest_id = char_id;
        put_pm(c, &m);
    }
}

/// intif.cpp intif_Mail_requestinbox (0x3048).
fn intif_mail_requestinbox(char_id: u32, flag: u8, kind: u8) {
    to_char(MailInter::RequestInbox { char_id, flag, kind });
}

/// intif.cpp intif_mail_checkreceiver: an online character answers at once, otherwise the char server is asked.
fn intif_mail_checkreceiver(c: &Ctx, aid: u32, name: &str) -> bool {
    let tsd = pc::nick2sd(c, name, false);
    if let Some(mut m) = pm(c, aid) {
        m.dest_id = 0;
        put_pm(c, &m);
    }
    // If the target player is online on this map-server
    if let Some(t) = tsd {
        let tch = pc::character_of(c, t.id);
        let (tcid, class, lv) = tch.map(|ch| (ch.id, ch.class, ch.base_level)).unwrap_or((0, 0, 0));
        clif_mail_receiver_ack(c, aid, tcid, class as i16, lv as u32, name);
        if let Some(mut m) = pm(c, aid) {
            m.dest_id = tcid;
            put_pm(c, &m);
        }
        return true;
    }
    let Some(p) = pc::get(c, aid) else { return false };
    to_char(MailInter::ReceiverCheck { char_id: p.char_id, name: int_mail::strncpy(name, NAME_LENGTH) });
    true
}

// ---------------------------------------------------------------------------------------------- clif senders

/// clif.cpp clif_Mail_setattachment: ZC_ACK_ADD_ITEM_RODEX (0x0b3f), all zero but the result when it failed.
fn clif_mail_setattachment(c: &Ctx, aid: u32, index: i32, amount: i32, flag: u8) {
    let mut p = PACKET_ZC_ACK_ADD_ITEM_RODEX { optionData: vec![ItemOptions::default(); 5], ..Default::default() };
    if flag == 0 {
        if let Some(r) = inv::inv_get(c, aid, (index - 2).max(0) as usize).filter(|_| index >= 2) {
            let it = &r.item;
            p.index = index as i16;
            p.count = amount as i16;
            p.itemId = crate::db::items::client_nameid(it.nameid);
            p.r#type = crate::clif_items::itemtype(it.nameid) as i8;
            p.IsIdentified = (it.identify != 0) as i8;
            p.IsDamaged = (it.attribute != 0) as i8;
            p.refiningLevel = it.refine as i8;
            p.slot = crate::clif_items::addcards(Some(it));
            p.optionData = crate::clif_items::add_random_options(it);
            p.weight = attachment_weight(c, aid, false) as i16;
            p.favorite = (it.favorite != 0) as u8;
            p.location = crate::pc_equip::pc_equippoint(c, aid, it);
            p.grade = it.enchantgrade as i8;
        }
    }
    p.PacketType = PACKET_ZC_ACK_ADD_ITEM_RODEX::ID as i16;
    p.result = flag as i8;
    send_self(c, aid, &p.to_vec());
}

/// The attachments' weight as the client shows it: sum of amount * (weight / 10); `stop_at_empty` is
/// clif_mail_removeitem's loop (it ends at the first empty entry and skips bad indexes).
fn attachment_weight(c: &Ctx, aid: u32, stop_at_empty: bool) -> i32 {
    let Some(m) = pm(c, aid) else { return 0 };
    let mut total: i32 = 0;
    for e in &m.item {
        if e.nameid == 0 {
            if stop_at_empty {
                break;
            }
            continue;
        }
        if e.index < 0 || e.index >= MAX_INVENTORY as i32 {
            continue;
        }
        let Some(r) = inv::inv_get(c, aid, e.index as usize) else { continue };
        total = total.wrapping_add(e.amount.wrapping_mul(inv::data_of(&r.item).weight as i32 / 10));
    }
    total
}

/// clif.cpp clif_mail_getattachment: ZC_ACK_ZENY_FROM_MAIL / ZC_ACK_ITEM_FROM_MAIL, then the inbox list again.
fn clif_mail_getattachment(c: &Ctx, aid: u32, slot: usize, result: u8, kind: u8) {
    if kind != MAIL_ATT_ITEM && kind != MAIL_ATT_ZENY {
        return;
    }
    let Some(m) = pm(c, aid) else { return };
    let msg = m.inbox.msg.get(slot).cloned().unwrap_or_default();
    let p = PACKET_ZC_ACK_ITEM_FROM_MAIL {
        PacketType: (if kind == MAIL_ATT_ZENY { RODEXGETZENY } else { RODEXGETITEM }) as i16,
        MailID: msg.id as i64,
        opentype: msg.kind as i8,
        result: result as i8,
    };
    send_self(c, aid, &p.to_vec());
    clif_mail_refreshinbox(c, aid, msg.kind, 0);
}

/// clif.cpp clif_Mail_send: ZC_WRITE_MAIL_RESULT.
fn clif_mail_send(c: &Ctx, aid: u32, result: i8) {
    let p = PACKET_ZC_WRITE_MAIL_RESULT { PacketType: RODEXWRITERESULT as i16, result };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_mail_delete: ZC_ACK_DELETE_MAIL, only on success.
fn clif_mail_delete(c: &Ctx, aid: u32, msg: &MailMessage, success: bool) {
    // This is only a success notification
    if success {
        let p = PACKET_ZC_ACK_DELETE_MAIL { PacketType: RODEXDELETE as i16, opentype: msg.kind as i8, MailID: msg.id as i64 };
        send_self(c, aid, &p.to_vec());
    }
}

/// clif.cpp clif_Mail_return: ZC_ACK_MAIL_RETURN (0x0274 <mail id>.L <result>.W).
fn clif_mail_return(c: &Ctx, aid: u32, mail_id: i32, fail: i16) {
    let mut w = Vec::with_capacity(8);
    w.extend_from_slice(&0x0274u16.to_le_bytes());
    w.extend_from_slice(&mail_id.to_le_bytes());
    w.extend_from_slice(&fail.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_Mail_new: ZC_NOTIFY_UNREADMAIL (0x09e7), 1 while there are unread or unchecked mails.
pub fn clif_mail_new(c: &Ctx, aid: u32) {
    let Some(m) = pm(c, aid) else { return };
    let p = PACKET_ZC_NOTIFY_UNREADMAIL { PacketType: RODEXICON as i16, result: (m.inbox.unread > 0 || m.inbox.unchecked > 0) as u8 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_Mail_window: ZC_MAIL_WINDOWS (0x0260 <type>.L).
fn clif_mail_window(c: &Ctx, aid: u32, flag: i32) {
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x0260u16.to_le_bytes());
    w.extend_from_slice(&flag.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_Mail_refreshinbox (PACKETVER >= 20170419): ZC_ACK_MAIL_LIST3 (0x0ac2) with every mail, newest
/// first, those past their scheduled deletion left out.
pub fn clif_mail_refreshinbox(c: &Ctx, aid: u32, _kind: u8, _mail_id: i64) {
    if mail_daily_count() != 0 {
        mail_refresh_remaining_amount(c, aid);
    }
    let Some(m) = pm(c, aid) else { return };
    let md = &m.inbox;
    let now = c.tick() / 1000;
    // Always send all: from msg[md->amount] down to msg[0]. (With a full inbox md->amount is MAIL_MAX_INBOX and
    // rAthena reads one entry past the array; here that entry is empty.)
    let start = md.amount.max(0) as usize;
    let mut w = Vec::new();
    w.extend_from_slice(&RODEXMAILLIST.to_le_bytes());
    w.extend_from_slice(&0u16.to_le_bytes());
    w.push(1); // Unknown
    for i in (0..=start).rev() {
        let Some(msg) = md.msg.get(i) else { continue };
        if msg.id < 1 {
            continue;
        }
        if msg.scheduled_deletion > 0 && msg.scheduled_deletion <= now {
            continue;
        }
        w.push(msg.kind);
        w.extend_from_slice(&(msg.id as u64).to_le_bytes());
        w.push((msg.status != MAIL_UNREAD) as u8);
        let mut mail_type = MAIL_TYPE_TEXT; // Normal letter
        if msg.zeny > 0 {
            mail_type |= MAIL_TYPE_ZENY; // Mail contains zeny
        }
        if msg.item.iter().any(|it| it.nameid > 0) {
            mail_type |= MAIL_TYPE_ITEM; // Mail contains items
        }
        // If it came from an npc?
        if msg.send_id == 0 {
            mail_type |= MAIL_TYPE_NPC;
        }
        w.push(mail_type);
        let mut name = latin1(&msg.send_name);
        name.truncate(NAME_LENGTH - 1);
        name.resize(NAME_LENGTH, 0);
        w.extend_from_slice(&name);
        // If automatic return/deletion of mails is enabled, notify the client when it will kick in
        let expires: u32 = if msg.scheduled_deletion > 0 {
            (msg.scheduled_deletion as u32).wrapping_sub(now as u32)
        } else {
            // Fake the scheduled deletion to one year in the future
            365 * 24 * 60 * 60
        };
        w.extend_from_slice(&expires.to_le_bytes());
        let title = latin1(&msg.title);
        let title_length = title.len() + 1;
        w.extend_from_slice(&(title_length as i16).to_le_bytes());
        w.extend_from_slice(&title);
        w.push(0);
    }
    let len = w.len() as u16;
    w[2..4].copy_from_slice(&len.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_Mail_read: ZC_ACK_READ_RODEX (text and attachments); an unread mail becomes read (the inbox list is
/// sent again through clif_parse_Mail_refreshinbox with the request that is being handled, `pkt`).
fn clif_mail_read(c: &Ctx, aid: u32, mail_id: i32, pkt: &[u8]) {
    let Some(mut m) = pm(c, aid) else { return };
    let Some(i) = m.inbox.msg.iter().position(|x| x.id == mail_id) else {
        clif_mail_return(c, aid, mail_id, 1); // Mail doesn't exist
        let name = sd_of(c, aid).map(|u| u.name).unwrap_or_default();
        log::warn!("clif_parse_Mail_read: char '{name}' trying to read a message not the inbox.");
        return;
    };
    if m.inbox.msg[i].body.is_empty() {
        m.inbox.msg[i].body = "(no message)".into(); // TODO (rAthena): confirm for RODEX
        put_pm(c, &m);
    }
    let msg = m.inbox.msg[i].clone();
    let text = latin1(&msg.body);
    let msg_len = text.len() + 1; // Zero Termination
    let mut subs = Vec::new();
    let mut cnt: i8 = 0;
    for it in msg.item.iter().take(MAIL_MAX_ITEM) {
        let Some(data) = crate::db::items::itemdb_exists(it.nameid) else { continue };
        if it.nameid > 0 && it.amount > 0 {
            let s = PACKET_ZC_ACK_READ_RODEX_SUB {
                count: it.amount,
                ITID: crate::db::items::client_nameid(it.nameid),
                IsIdentified: (it.identify != 0) as i8,
                IsDamaged: (it.attribute != 0) as i8,
                slot: crate::clif_items::addcards(Some(it)),
                location: crate::pc_equip::pc_equippoint_sub(c, aid, data),
                r#type: crate::clif_items::itemtype(it.nameid),
                viewSprite: data.look as u16,
                bindOnEquip: if it.bound != 0 { 2 } else if data.flag.bind_on_equip { 1 } else { 0 },
                option_data: crate::clif_items::add_random_options(it),
                refiningLevel: it.refine as i8,
                grade: it.enchantgrade as i8,
            };
            s.write(&mut subs);
            cnt += 1;
        }
    }
    let total = PACKET_ZC_ACK_READ_RODEX::SIZE + msg_len + subs.len();
    let mut w = Vec::with_capacity(total);
    w.extend_from_slice(&PACKET_ZC_ACK_READ_RODEX::ID.to_le_bytes());
    w.extend_from_slice(&(total as i16).to_le_bytes());
    w.push(msg.kind);
    w.extend_from_slice(&(msg.id as i64).to_le_bytes());
    w.extend_from_slice(&(msg_len as i16).to_le_bytes());
    w.extend_from_slice(&(msg.zeny as i64).to_le_bytes());
    w.push(cnt as u8);
    w.extend_from_slice(&text);
    w.push(0);
    w.extend_from_slice(&subs);
    send_self(c, aid, &w);
    if msg.status == MAIL_UNREAD {
        if let Some(mut m) = pm(c, aid) {
            m.inbox.msg[i].status = MAIL_READ;
            put_pm(c, &m);
        }
        to_char(MailInter::Read { mail_id });
        parse_mail_refreshinbox(c, aid, pkt);
        if let Some(mut m) = pm(c, aid) {
            m.inbox.unread -= 1;
            put_pm(c, &m);
        }
        clif_mail_new(c, aid);
    }
}

/// clif.cpp clif_send_Mail_beginwrite_ack: ZC_ACK_OPEN_WRITE_MAIL.
fn clif_send_mail_beginwrite_ack(c: &Ctx, aid: u32, name: &str, success: bool) {
    let p = PACKET_ZC_ACK_OPEN_WRITE_MAIL { PacketType: RODEXOPENWRITE as i16, receiveName: int_mail::strncpy(name, NAME_LENGTH), result: success as i8 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_Mail_Receiver_Ack: ZC_CHECKNAME (0x0a51).
fn clif_mail_receiver_ack(c: &Ctx, aid: u32, char_id: u32, class: i16, level: u32, name: &str) {
    let p = PACKET_ZC_CHECKNAME {
        PacketType: PACKET_ZC_CHECKNAME::ID as i16,
        CharId: char_id as i32,
        Class: class,
        BaseLevel: level as i16,
        Name: name.chars().take(NAME_LENGTH).collect(),
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_mail_removeitem: ZC_ACK_REMOVE_ITEM_MAIL.
fn clif_mail_removeitem(c: &Ctx, aid: u32, success: bool, index: i32, amount: i32) {
    let p = PACKET_ZC_ACK_REMOVE_ITEM_MAIL {
        PacketType: RODEXREMOVEITEM as i16,
        result: success as i8,
        index: index as i16,
        cnt: amount as u16,
        weight: attachment_weight(c, aid, true) as i16,
    };
    send_self(c, aid, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- clif parsers

fn rq(pkt: &[u8], off: usize) -> u64 {
    Reader::at(pkt, off).u64().unwrap_or(0)
}

fn rw(pkt: &[u8], off: usize) -> u16 {
    Reader::at(pkt, off).u16().unwrap_or(0)
}

fn cmd_of(pkt: &[u8]) -> u16 {
    rw(pkt, 0)
}

/// The latin-1 C string at `off` (at most `n` - 1 bytes: safestrncpy).
fn cstr(pkt: &[u8], off: usize, n: usize) -> String {
    let b = pkt.get(off..).unwrap_or(&[]);
    let b = &b[..b.len().min(n.saturating_sub(1))];
    from_latin1(until_nul(b))
}

/// clif.cpp clif_parse_Mail_refreshinbox (0x09e8 / 0x09ee / 0x09ef / 0x0ac0 / 0x0ac1; PACKETVER >= 20170419 reads
/// the mail id at offset 2 whatever the packet).
pub fn parse_mail_refreshinbox(c: &Ctx, aid: u32, pkt: &[u8]) {
    if mail_invalid_operation(c, aid) {
        return;
    }
    let cmd = cmd_of(pkt);
    let mail_id = rq(pkt, 2);
    let Some(m) = pm(c, aid) else { return };
    let open_type = match m.inbox.msg.iter().find(|x| x.id as i64 as u64 == mail_id) {
        None => MAIL_INBOX_NORMAL,
        Some(x) => x.kind,
    };
    let mail_id: i64 = 0;
    if !matches!(open_type, MAIL_INBOX_NORMAL | MAIL_INBOX_ACCOUNT | MAIL_INBOX_RETURNED) {
        // Unknown type: ignore request
        return;
    }
    if m.changed || cmd == 0x09ef || cmd == 0x0ac1 {
        intif_mail_requestinbox(m.char_id, 1, open_type);
        return;
    }
    // (If it is not a next page request, the mail id is 0: it already is)
    clif_mail_refreshinbox(c, aid, open_type, mail_id);
}

/// clif.cpp clif_parse_Mail_read (CZ_REQ_READ_MAIL 0x09ea: <mail tab>.B <mail id>.Q).
pub fn parse_mail_read(c: &Ctx, aid: u32, pkt: &[u8]) {
    let mail_id = rq(pkt, 3) as i32;
    if mail_id <= 0 {
        return;
    }
    if mail_invalid_operation(c, aid) {
        return;
    }
    clif_mail_read(c, aid, mail_id, pkt);
}

/// clif.cpp clif_parse_Mail_beginwrite (CZ_REQ_OPEN_WRITE_MAIL 0x0a08).
pub fn parse_mail_beginwrite(c: &Ctx, aid: u32, pkt: &[u8]) {
    let name = cstr(pkt, 2, NAME_LENGTH);
    if mail_invalid_operation(c, aid) {
        return;
    }
    let Some(p) = pc::get(c, aid) else { return };
    if p.storage_flag != 0 || p.mail_writing || p.trading {
        clif_send_mail_beginwrite_ack(c, aid, &name, false);
        return;
    }
    mail_clear(c, aid);
    if let Some(mut p) = pc::get(c, aid) {
        p.mail_writing = true;
        pc::put(c, &p);
    }
    clif_send_mail_beginwrite_ack(c, aid, &name, true);
}

/// clif.cpp clif_parse_Mail_cancelwrite (CZ_REQ_CANCEL_WRITE_MAIL 0x0a03).
pub fn parse_mail_cancelwrite(c: &Ctx, aid: u32) {
    if let Some(mut p) = pc::get(c, aid) {
        p.mail_writing = false;
        pc::put(c, &p);
    }
}

/// clif.cpp clif_parse_Mail_Receiver_Check (CZ_CHECKNAME2 0x0b97 for PACKETVER_RE >= 20211103).
pub fn parse_mail_receiver_check(c: &Ctx, aid: u32, pkt: &[u8]) {
    if mail_invalid_operation(c, aid) {
        return;
    }
    let name = cstr(pkt, 2, NAME_LENGTH);
    intif_mail_checkreceiver(c, aid, &name);
}

/// clif.cpp clif_parse_Mail_getattach (CZ_REQ_ZENY_FROM_MAIL 0x09f1 / CZ_REQ_ITEM_FROM_MAIL 0x09f3).
pub fn parse_mail_getattach(c: &Ctx, aid: u32, pkt: &[u8]) {
    let packet_id = cmd_of(pkt);
    let mail_id = rq(pkt, 2) as i32;
    let mut attachment = match packet_id {
        0x09f1 => MAIL_ATT_ZENY,
        0x09f3 => MAIL_ATT_ITEM,
        _ => MAIL_ATT_NONE,
    };
    if mail_id <= 0 {
        return;
    }
    if mail_invalid_operation(c, aid) {
        return;
    }
    let Some(mut m) = pm(c, aid) else { return };
    let Some(i) = m.inbox.msg.iter().position(|x| x.id == mail_id) else { return };
    if attachment & MAIL_ATT_ZENY != 0 && m.inbox.msg[i].zeny < 1 {
        attachment &= !MAIL_ATT_ZENY;
    }
    if attachment & MAIL_ATT_ITEM != 0 {
        // No items were found
        if !m.inbox.msg[i].item.iter().any(|it| it.nameid > 0 || it.amount > 0) {
            attachment &= !MAIL_ATT_ITEM;
        }
    }
    // Either no attachment requested at all or there are no zeny or items in the mail
    if attachment == MAIL_ATT_NONE {
        return;
    }
    let Some(ch) = pc::character_of(c, aid) else { return };
    if attachment & MAIL_ATT_ZENY != 0 {
        let total = m.inbox.msg[i].zeny.wrapping_add(ch.zeny).wrapping_add(m.pending_zeny);
        if total > MAX_ZENY as u32 {
            clif_mail_getattachment(c, aid, i, 1, MAIL_ATT_ZENY); // too many zeny
            return;
        }
        // Store the pending zeny (required for the "retrieve all" feature)
        m.pending_zeny = m.pending_zeny.wrapping_add(m.inbox.msg[i].zeny);
        // To make sure another request fails
        m.inbox.msg[i].zeny = 0;
        put_pm(c, &m);
    }
    if attachment & MAIL_ATT_ITEM != 0 {
        let mut slots: i32 = 0;
        let mut totalweight: u32 = 0;
        for it in m.inbox.msg[i].item.clone() {
            if it.nameid > 0 && it.amount > 0 {
                let Some(data) = crate::db::items::itemdb_exists(it.nameid) else { continue };
                match inv::pc_checkadditem(c, aid, it.nameid, it.amount as i32) {
                    inv::CHKADDITEM_NEW => slots += data.inventory_slot_needed(it.amount as i32),
                    inv::CHKADDITEM_OVERAMOUNT => {
                        clif_mail_getattachment(c, aid, i, 2, MAIL_ATT_ITEM);
                        return;
                    }
                    _ => {}
                }
                totalweight = totalweight.wrapping_add(data.weight.wrapping_mul(it.amount as u32));
            }
        }
        let Some(s) = inv::st(c, aid) else { return };
        if totalweight.wrapping_add(s.weight).wrapping_add(m.pending_weight) > s.max_weight {
            clif_mail_getattachment(c, aid, i, 2, MAIL_ATT_ITEM);
            return;
        } else if (inv::pc_inventoryblank(c, aid) as i32) < slots + m.pending_slots as i32 {
            clif_mail_getattachment(c, aid, i, 2, MAIL_ATT_ITEM);
            return;
        }
        // To make sure another request fails
        m.inbox.msg[i].item = vec![Item::new(0); MAIL_MAX_ITEM];
        // Store the pending weight / slots (required for the "retrieve all" feature)
        m.pending_weight = m.pending_weight.wrapping_add(totalweight);
        m.pending_slots = m.pending_slots.wrapping_add(slots as u16);
        put_pm(c, &m);
    }
    to_char(MailInter::GetAttach { char_id: ch.id, mail_id, kind: attachment });
}

/// clif.cpp clif_parse_Mail_delete (CZ_REQ_DELETE_MAIL 0x09f5: <mail tab>.B <mail id>.Q).
pub fn parse_mail_delete(c: &Ctx, aid: u32, pkt: &[u8]) {
    let mail_id = rq(pkt, 3) as i32;
    if mail_id <= 0 {
        return;
    }
    if mail_invalid_operation(c, aid) {
        return;
    }
    let Some(mut m) = pm(c, aid) else { return };
    let Some(i) = m.inbox.msg.iter().position(|x| x.id == mail_id) else { return };
    let msg = m.inbox.msg[i].clone();
    // can't delete mail without removing zeny first
    if msg.zeny > 0 {
        clif_mail_delete(c, aid, &msg, false);
        return;
    }
    // can't delete mail without removing attachment(s) first
    if msg.item.iter().any(|it| it.nameid > 0 && it.amount > 0) {
        clif_mail_delete(c, aid, &msg, false);
        return;
    }
    to_char(MailInter::Delete { char_id: m.char_id, mail_id });
    if msg.status == MAIL_UNREAD {
        m.inbox.unread -= 1;
        put_pm(c, &m);
        clif_mail_new(c, aid);
    }
}

/// clif.cpp clif_parse_Mail_return (CZ_RODEX_RETURN 0x0b98 for PACKETVER_RE >= 20211103): "not supported for now".
pub fn parse_mail_return(_c: &Ctx, _aid: u32, _pkt: &[u8]) {}

/// clif.cpp clif_parse_Mail_setattach (CZ_ADD_ITEM_TO_MAIL 0x0a04: <index>.W <amount>.W).
pub fn parse_mail_setattach(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pos = crate::net::packets::packet_pos(cmd_of(pkt));
    let idx = rw(pkt, pos.first().copied().unwrap_or(2) as usize);
    let amount = rw(pkt, pos.get(1).copied().unwrap_or(4) as usize) as i32;
    // server_index(idx) >= MAX_INVENTORY; an index below 2 makes rAthena read inventory_data[-2 / -1] (memory before
    // the array): treated as an empty slot here
    let sidx = idx as i32 - 2;
    if amount < 0 || sidx >= MAX_INVENTORY as i32 {
        return;
    }
    if sidx < 0 || inv::inv_get(c, aid, sidx as usize).is_none() {
        return;
    }
    if mail_invalid_operation(c, aid) {
        return;
    }
    let flag = mail_setitem(c, aid, idx as i32, amount as u32);
    if flag == MAIL_ATTACH_EQUIPSWITCH {
        if let Some(sd) = sd_of(c, aid) {
            crate::clif_items::msg(c, &sd, MSI_SWAP_EQUIPITEM_UNREGISTER_FIRST);
        }
    } else {
        clif_mail_setattachment(c, aid, idx as i32, amount, flag);
    }
}

/// clif.cpp clif_parse_Mail_winopen (CZ_REQ_REMOVE_ITEM_MAIL 0x0a06: <index>.W <amount>.W).
pub fn parse_mail_winopen(c: &Ctx, aid: u32, pkt: &[u8]) {
    let index = rw(pkt, 2);
    let count = rw(pkt, 4);
    mail_removeitem(c, aid, false, index as i32, count as i32);
}

/// clif.cpp clif_parse_Mail_send (CZ_REQ_WRITE_MAIL2 0x0a6e / 0x09ec: header of 68 bytes for PACKETVER > 20160330).
pub fn parse_mail_send(c: &Ctx, aid: u32, pkt: &[u8]) {
    let length = rw(pkt, 2) as usize;
    const HEADER_LENGTH: usize = 68;
    if length < HEADER_LENGTH {
        log::warn!("Too short...");
        clif_mail_send(c, aid, WRITE_MAIL_FAILED);
        return;
    }
    if mail_invalid_operation(c, aid) {
        return;
    }
    // Forged request without a begin writing packet?
    if !pc::get(c, aid).map(|p| p.mail_writing).unwrap_or(false) {
        return; // Ignore it
    }
    let receiver = cstr(pkt, 4, NAME_LENGTH);
    let zeny = rq(pkt, 52);
    let title_length = rw(pkt, 60) as usize;
    let text_length = rw(pkt, 62) as usize;
    if title_length > length - HEADER_LENGTH || text_length > length - HEADER_LENGTH - title_length {
        log::warn!("Invalid Rodex mail content length from account {aid}.");
        clif_mail_send(c, aid, WRITE_MAIL_FAILED);
        return;
    }
    let real_title_length = title_length.min(MAIL_TITLE_LENGTH);
    let real_text_length = text_length.min(MAIL_BODY_LENGTH);
    // (safestrncpy with n = 0 leaves rAthena's stack buffer as it was; an empty string here)
    let title = cstr(pkt, HEADER_LENGTH, real_title_length);
    let text = cstr(pkt, HEADER_LENGTH + title_length, real_text_length);
    if zeny > 0 && mail_setitem(c, aid, 0, zeny as u32) != MAIL_ATTACH_SUCCESS {
        clif_mail_send(c, aid, WRITE_MAIL_FAILED);
        return;
    }
    mail_send(c, aid, &receiver, &title, &text, real_text_length as i32);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cstr_is_safestrncpy() {
        let pkt = b"\x00\x00abcdef\x00zz";
        assert_eq!(cstr(pkt, 2, 4), "abc");
        assert_eq!(cstr(pkt, 2, 24), "abcdef");
        assert_eq!(cstr(pkt, 2, 0), "");
    }
}
