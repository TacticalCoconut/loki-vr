//! char/int_mail.cpp: the char server's half of RoDEX mail (the `mail` / `mail_attachments` SQL tables and the
//! inter-server messages 0x3048-0x304e / 0x3848-0x384e), inside the module.
//!
//! rAthena keeps mails in two SQL tables and talks to the map server over the inter link; here the mails are one
//! table ([`MailRow`], the attachments folded into the row with their `index` column) and the inter messages go
//! through intif.rs's queue ([`MailInter`], one `Inter::Mail` variant), handled after the map-server function that
//! sent them, exactly as the two processes see them on their one TCP link. The map server's half is mail.rs.
//!
//! Deliberate differences (no SQL, one map server):
//! - mail ids come from the module's id counter (`ids::next("mail_id", 1)`), as MySQL's AUTO_INCREMENT does on a
//!   fresh `mail` table;
//! - char names are looked up through `character.name_key` (MySQL's case-insensitive collation);
//! - "is the character online on a map server" (char_get_onlinedb, server >= 0) is "has a `pc` row";
//! - the char server's two interval timers (do_init: mail_return_timer / mail_delete_timer, every minute) are armed by
//!   [`ensure_timers`] the first time the module sees a login or a mail, and live on in the timer table.

use spacetimedb::{table, SpacetimeType, Table};

use crate::character::character;
use crate::intif::{self, Inter};
use crate::inventory::Item;
use crate::timer::{self, Func};
use crate::world::Ctx;

/// mmo.hpp MAIL_MAX_INBOX, MAIL_TITLE_LENGTH, MAIL_BODY_LENGTH / MAIL_MAX_ITEM (PACKETVER >= 20150513),
/// MAIL_PAGE_SIZE, MAIL_ITERATION_SIZE.
pub const MAIL_MAX_INBOX: usize = 30;
pub const MAIL_TITLE_LENGTH: usize = 40;
pub const MAIL_BODY_LENGTH: usize = 500;
pub const MAIL_MAX_ITEM: usize = 5;
pub const MAIL_PAGE_SIZE: usize = 7;
pub const MAIL_ITERATION_SIZE: usize = 100;
/// mmo.hpp NAME_LENGTH.
pub const NAME_LENGTH: usize = 24;

/// mmo.hpp enum mail_status.
pub const MAIL_NEW: u8 = 0;
pub const MAIL_UNREAD: u8 = 1;
pub const MAIL_READ: u8 = 2;

/// mmo.hpp enum mail_inbox_type.
pub const MAIL_INBOX_NORMAL: u8 = 0;
pub const MAIL_INBOX_ACCOUNT: u8 = 1;
pub const MAIL_INBOX_RETURNED: u8 = 2;

/// mmo.hpp enum mail_attachment_type.
pub const MAIL_ATT_NONE: u8 = 0;
pub const MAIL_ATT_ZENY: u8 = 1;
pub const MAIL_ATT_ITEM: u8 = 2;
pub const MAIL_ATT_ALL: u8 = MAIL_ATT_ZENY | MAIL_ATT_ITEM;

/// conf/char_athena.conf `mail_return_days: 15`, `mail_delete_days: 15`, `mail_retrieve: yes`,
/// `mail_return_empty: yes` (Loki's char_conf import changes none of them).
pub const MAIL_RETURN_DAYS: i64 = 15;
pub const MAIL_DELETE_DAYS: i64 = 15;
pub const MAIL_RETRIEVE: bool = true;
pub const MAIL_RETURN_EMPTY: bool = true;

/// mmo.hpp struct mail_message (map and char server). Text fields are latin-1 code points, as every name in the
/// module; `item` always has MAIL_MAX_ITEM entries.
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct MailMessage {
    pub id: i32,
    /// char id of the sender (0: sent by the server / a script).
    pub send_id: u32,
    pub send_name: String,
    /// char id of the receiver.
    pub dest_id: u32,
    pub dest_name: String,
    pub title: String,
    pub body: String,
    /// enum mail_inbox_type.
    pub kind: u8,
    /// time_t (0: never).
    pub scheduled_deletion: i64,
    /// enum mail_status.
    pub status: u8,
    /// time_t when the message was sent.
    pub timestamp: i64,
    pub zeny: u32,
    pub item: Vec<Item>,
}

impl Default for MailMessage {
    fn default() -> Self {
        MailMessage::new()
    }
}

impl MailMessage {
    /// `struct mail_message msg; memset(&msg, 0, sizeof(msg))`.
    pub fn new() -> Self {
        MailMessage {
            id: 0,
            send_id: 0,
            send_name: String::new(),
            dest_id: 0,
            dest_name: String::new(),
            title: String::new(),
            body: String::new(),
            kind: MAIL_INBOX_NORMAL,
            scheduled_deletion: 0,
            status: MAIL_NEW,
            timestamp: 0,
            zeny: 0,
            item: vec![Item::new(0); MAIL_MAX_ITEM],
        }
    }
    pub fn item_at(&self, i: usize) -> Item {
        self.item.get(i).cloned().unwrap_or_else(|| Item::new(0))
    }
}

/// mmo.hpp struct mail_data (sd->mail.inbox): `msg` always has MAIL_MAX_INBOX entries (id 0 = empty).
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct MailData {
    pub amount: i16,
    pub full: bool,
    pub unchecked: i16,
    pub unread: i16,
    pub msg: Vec<MailMessage>,
}

impl Default for MailData {
    fn default() -> Self {
        MailData { amount: 0, full: false, unchecked: 0, unread: 0, msg: vec![MailMessage::new(); MAIL_MAX_INBOX] }
    }
}

/// One `mail_attachments` row (its `index` column and the item columns).
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct MailAttachment {
    pub index: u16,
    pub item: Item,
}

/// rAthena `mail` table (sql-files/main.sql) with its `mail_attachments` rows.
#[table(accessor = mail, index(accessor = by_dest, btree(columns = [dest_id])))]
#[derive(Clone, Debug)]
pub struct MailRow {
    #[primary_key]
    pub id: i32,
    pub send_name: String,
    pub send_id: u32,
    pub dest_name: String,
    pub dest_id: u32,
    pub title: String,
    pub message: String,
    /// unix seconds.
    pub time: i64,
    pub status: u8,
    pub zeny: u32,
    pub kind: u8,
    pub attachments: Vec<MailAttachment>,
}

/// The char server's mail timers (do_init's add_timer_interval ids).
#[table(accessor = mail_char_state)]
#[derive(Clone, Debug)]
pub struct MailCharState {
    #[primary_key]
    pub key: u8,
    pub return_timer: u64,
    pub delete_timer: u64,
}

/// The mail messages on the inter link (intif.cpp / int_mail.cpp packet ids in the docs).
#[derive(Clone, Debug)]
pub enum MailInter {
    // ---- map → char (inter_mail_parse_frommap)
    /// 0x3048 intif_Mail_requestinbox (flag 0: update, 1: open the mail box).
    RequestInbox { char_id: u32, flag: u8, kind: u8 },
    /// 0x3049 intif_Mail_read.
    Read { mail_id: i32 },
    /// 0x304a intif_mail_getattach.
    GetAttach { char_id: u32, mail_id: i32, kind: u8 },
    /// 0x304b intif_Mail_delete.
    Delete { char_id: u32, mail_id: i32 },
    /// 0x304c intif_Mail_return.
    Return { char_id: u32, mail_id: i32 },
    /// 0x304d intif_Mail_send.
    Send { account_id: u32, msg: MailMessage },
    /// 0x304e intif_mail_checkreceiver.
    ReceiverCheck { char_id: u32, name: String },
    // ---- char → map (intif_parse)
    /// 0x3848 mapif_Mail_sendinbox.
    InboxReceived { char_id: u32, flag: u8, kind: u8, md: MailData },
    /// 0x3849 mapif_Mail_new (to every map server).
    New { dest_id: u32, mail_id: i32, send_name: String, title: String, kind: u8 },
    /// 0x384a mapif_Mail_getattach.
    GetAttachAck { char_id: u32, mail_id: i32, zeny: i32, items: Vec<Item> },
    /// 0x384b mapif_Mail_delete.
    DeleteAck { char_id: u32, mail_id: i32, failed: bool },
    /// 0x384c mapif_Mail_return.
    ReturnAck { char_id: u32, mail_id: i32, fail: bool },
    /// 0x384d mapif_Mail_send.
    SendAck { msg: MailMessage },
    /// 0x384e mapif_Mail_receiver_send.
    Receiver { requesting_char_id: u32, char_id: u32, class: u16, base_level: u16, name: String },
}

/// intif.rs's dispatch for [`Inter::Mail`]: char/inter.cpp inter_mail_parse_frommap for requests, map/intif.cpp
/// intif_parse for answers.
pub fn dispatch(c: &Ctx, m: MailInter) {
    match m {
        MailInter::RequestInbox { char_id, flag, kind } => mapif_parse_mail_requestinbox(c, char_id, flag, kind),
        MailInter::Read { mail_id } => mapif_parse_mail_read(c, mail_id),
        MailInter::GetAttach { char_id, mail_id, kind } => mapif_mail_getattach(c, char_id, mail_id, kind),
        MailInter::Delete { char_id, mail_id } => {
            mapif_mail_delete(c, true, char_id, mail_id, 0);
        }
        MailInter::Return { char_id, mail_id } => mapif_mail_return(c, true, char_id, mail_id, 0, 0),
        MailInter::Send { account_id, msg } => mapif_parse_mail_send(c, account_id, msg),
        MailInter::ReceiverCheck { char_id, name } => mapif_parse_mail_receiver_check(c, char_id, &name),
        MailInter::InboxReceived { char_id, flag, kind, md } => crate::mail::intif_parse_mail_inboxreceived(c, char_id, flag, kind, md),
        MailInter::New { dest_id, mail_id, send_name, title, kind } => crate::mail::intif_parse_mail_new(c, dest_id, mail_id, &send_name, &title, kind),
        MailInter::GetAttachAck { char_id, mail_id, zeny, items } => crate::mail::intif_parse_mail_getattach(c, char_id, mail_id, zeny, items),
        MailInter::DeleteAck { char_id, mail_id, failed } => crate::mail::intif_parse_mail_delete(c, char_id, mail_id, failed),
        MailInter::ReturnAck { char_id, mail_id, fail } => crate::mail::intif_parse_mail_return(c, char_id, mail_id, fail),
        MailInter::SendAck { msg } => crate::mail::intif_parse_mail_send(c, msg),
        MailInter::Receiver { requesting_char_id, char_id, class, base_level, name } => {
            crate::mail::intif_parse_mail_receiver(c, requesting_char_id, char_id, class, base_level, &name)
        }
    }
}

fn to_map(m: MailInter) {
    intif::send(Inter::Mail(m));
}

/// time(nullptr).
fn now(c: &Ctx) -> i64 {
    c.tick() / 1000
}

/// safestrncpy(dst, src, n): at most n - 1 bytes (latin-1 code points here).
pub fn strncpy(s: &str, n: usize) -> String {
    s.chars().take(n.saturating_sub(1)).collect()
}

// ---------------------------------------------------------------------------------------------- timers

/// char.cpp do_init: `add_timer_interval(gettick() + 1000, mail_return_timer, 0, 0, 1 * 60 * 1000)` and the same for
/// mail_delete_timer (see the module doc for when the module arms them).
pub fn ensure_timers(c: &Ctx) {
    // char.cpp do_init → inter_init_sql also loads the auctions (inter_auction_sql_init)
    crate::int_auction::ensure_loaded(c);
    let s = c.db().mail_char_state().key().find(0);
    let ok = |id: u64| id != 0 && timer::get(c, id).is_some();
    if let Some(s) = &s {
        if ok(s.return_timer) && ok(s.delete_timer) {
            return;
        }
    }
    let mut row = s.clone().unwrap_or(MailCharState { key: 0, return_timer: 0, delete_timer: 0 });
    if !ok(row.return_timer) {
        row.return_timer = timer::add_interval(c, c.tick() + 1000, Func::MailReturnTimer, 0, 0, 60 * 1000);
    }
    if !ok(row.delete_timer) {
        row.delete_timer = timer::add_interval(c, c.tick() + 1000, Func::MailDeleteTimer, 0, 0, 60 * 1000);
    }
    if s.is_some() {
        c.db().mail_char_state().key().update(row);
    } else {
        c.db().mail_char_state().insert(row);
    }
}

/// int_mail.cpp mail_return_timer.
pub fn mail_return_timer(c: &Ctx) {
    mail_timer_sub(c, MAIL_RETURN_DAYS, MAIL_INBOX_NORMAL);
}

/// int_mail.cpp mail_delete_timer.
pub fn mail_delete_timer(c: &Ctx) {
    mail_timer_sub(c, MAIL_DELETE_DAYS, MAIL_INBOX_RETURNED);
}

/// int_mail.cpp mail_timer_sub: mails of `kind` older than `limit` days are returned (normal mails) or deleted
/// (returned mails), at most MAIL_ITERATION_SIZE per run; mails sent by the server (send_id 0) are deleted outright.
fn mail_timer_sub(c: &Ctx, limit: i64, kind: u8) {
    // Server is configured to never delete or return mails
    if limit == 0 {
        return;
    }
    // `time` <= UNIX_TIMESTAMP( NOW() - INTERVAL limit DAY )
    let cutoff = now(c) - limit * 24 * 60 * 60;
    // Start by deleting all expired mails sent by the server
    let gone: Vec<i32> = c.db().mail().iter().filter(|m| m.kind == kind && m.send_id == 0 && m.time <= cutoff).map(|m| m.id).collect();
    for id in gone {
        c.db().mail().id().delete(id);
    }
    if limit <= 0 {
        return;
    }
    // SELECT m.id, c.char_id, c.account_id, c2.account_id FROM mail m INNER JOIN char c ON c.char_id = m.dest_id
    // INNER JOIN char c2 ON c2.char_id = m.send_id WHERE type = ? AND time <= ? ORDER BY id LIMIT MAIL_ITERATION_SIZE + 1
    let mut rows: Vec<MailRow> = c.db().mail().iter().filter(|m| m.kind == kind && m.time <= cutoff).collect();
    rows.sort_by_key(|m| m.id);
    let mut mails: Vec<(i32, u32, u32, u32)> = Vec::new();
    for m in rows {
        let Some(dest) = c.db().character().id().find(m.dest_id) else { continue };
        let Some(sender) = c.db().character().id().find(m.send_id) else { continue };
        mails.push((m.id, dest.id, dest.account_id, sender.account_id));
        if mails.len() >= MAIL_ITERATION_SIZE {
            break;
        }
    }
    for (mail_id, char_id, account_id, account_id_sender) in mails {
        if kind == MAIL_INBOX_NORMAL {
            mapif_mail_return(c, false, char_id, mail_id, account_id, account_id_sender);
        } else if kind == MAIL_INBOX_RETURNED {
            mapif_mail_delete(c, false, char_id, mail_id, account_id);
        }
    }
}

// ---------------------------------------------------------------------------------------------- SQL

/// int_mail.cpp mail_fromsql: the inbox of a character (the first MAIL_MAX_INBOX mails by id); new mails become
/// unread (and count as unchecked).
pub fn mail_fromsql(c: &Ctx, char_id: u32) -> MailData {
    let mut md = MailData::default();
    // SELECT id FROM mail WHERE dest_id = ? AND status < 3 ORDER BY id LIMIT MAIL_MAX_INBOX + 1
    let mut ids: Vec<i32> = c.db().mail().by_dest().filter(char_id).filter(|m| m.status < 3).map(|m| m.id).collect();
    ids.sort_unstable();
    ids.truncate(MAIL_MAX_INBOX + 1);
    md.full = ids.len() > MAIL_MAX_INBOX;
    let n = ids.len().min(MAIL_MAX_INBOX);
    for (i, id) in ids.iter().take(n).enumerate() {
        match mail_loadmessage(c, *id) {
            Some(m) => md.msg[i] = m,
            // (a mail that fails to load ends the load with what rAthena has read so far)
            None => return md,
        }
    }
    md.amount = n as i16;
    md.unchecked = 0;
    md.unread = 0;
    for i in 0..n {
        let msg = &mut md.msg[i];
        if msg.status == MAIL_NEW {
            if let Some(r) = c.db().mail().id().find(msg.id) {
                c.db().mail().id().update(MailRow { status: MAIL_UNREAD, ..r });
            }
            msg.status = MAIL_UNREAD;
            md.unchecked += 1;
        } else if msg.status == MAIL_UNREAD {
            md.unread += 1;
        }
    }
    log::info!("mail load complete from DB - id: {char_id} (total: {})", md.amount);
    md
}

/// int_mail.cpp mail_savemessage: stores one message (the attachments with a nameid, by their index); returns its
/// id (0 on failure).
pub fn mail_savemessage(c: &Ctx, msg: &mut MailMessage) -> i32 {
    let id = crate::ids::next(c.rc, "mail_id", 1) as i32;
    let mut attachments = Vec::new();
    for (i, it) in msg.item.iter().enumerate().take(MAIL_MAX_ITEM) {
        // skip empty and already matched entries
        if it.nameid == 0 {
            continue;
        }
        attachments.push(MailAttachment { index: i as u16, item: sql_item(it) });
    }
    c.db().mail().insert(MailRow {
        id,
        send_name: strncpy(&msg.send_name, NAME_LENGTH + 1),
        send_id: msg.send_id,
        dest_name: strncpy(&msg.dest_name, NAME_LENGTH + 1),
        dest_id: msg.dest_id,
        title: strncpy(&msg.title, MAIL_TITLE_LENGTH + 1),
        message: strncpy(&msg.body, MAIL_BODY_LENGTH + 1),
        time: msg.timestamp,
        status: msg.status,
        zeny: msg.zeny,
        kind: msg.kind,
        attachments,
    });
    msg.id = id;
    id
}

/// The columns of `mail_attachments` (amount, nameid, refine, attribute, identify, unique_id, bound, enchantgrade,
/// card0-3, option_id/val/parm 0-4): everything else of the item is not stored.
fn sql_item(it: &Item) -> Item {
    let mut o = Item::new(it.nameid);
    o.amount = it.amount;
    o.refine = it.refine;
    o.attribute = it.attribute;
    o.identify = it.identify;
    o.unique_id = it.unique_id;
    o.bound = it.bound;
    o.enchantgrade = it.enchantgrade;
    o.card0 = it.card0;
    o.card1 = it.card1;
    o.card2 = it.card2;
    o.card3 = it.card3;
    o.option = it.options().to_vec();
    o
}

/// int_mail.cpp mail_loadmessage: one message with its attachments (by index, compacted into item[0..]); the
/// scheduled deletion comes from mail_return_days / mail_delete_days.
pub fn mail_loadmessage(c: &Ctx, mail_id: i32) -> Option<MailMessage> {
    let r = c.db().mail().id().find(mail_id)?;
    let mut msg = MailMessage::new();
    msg.id = r.id;
    msg.send_name = r.send_name.clone();
    msg.send_id = r.send_id;
    msg.dest_name = r.dest_name.clone();
    msg.dest_id = r.dest_id;
    msg.title = r.title.clone();
    msg.body = r.message.clone();
    msg.timestamp = r.time;
    msg.status = r.status;
    msg.zeny = r.zeny;
    msg.kind = r.kind;
    msg.scheduled_deletion = if msg.kind == MAIL_INBOX_NORMAL && MAIL_RETURN_DAYS > 0 {
        msg.timestamp + MAIL_RETURN_DAYS * 24 * 60 * 60
    } else if msg.kind == MAIL_INBOX_RETURNED && MAIL_DELETE_DAYS > 0 {
        msg.timestamp + MAIL_DELETE_DAYS * 24 * 60 * 60
    } else {
        0
    };
    let mut att = r.attachments.clone();
    att.sort_by_key(|a| a.index);
    for (i, a) in att.iter().take(MAIL_MAX_ITEM).enumerate() {
        let mut it = sql_item(&a.item);
        it.expire_time = 0;
        msg.item[i] = it;
    }
    Some(msg)
}

// ---------------------------------------------------------------------------------------------- inter requests

/// int_mail.cpp mapif_Mail_sendinbox / mapif_parse_Mail_requestinbox.
fn mapif_parse_mail_requestinbox(c: &Ctx, char_id: u32, flag: u8, kind: u8) {
    let md = mail_fromsql(c, char_id);
    to_map(MailInter::InboxReceived { char_id, flag, kind, md });
}

/// int_mail.cpp mapif_parse_Mail_read: mark the mail as read.
fn mapif_parse_mail_read(c: &Ctx, mail_id: i32) {
    if let Some(r) = c.db().mail().id().find(mail_id) {
        c.db().mail().id().update(MailRow { status: MAIL_READ, ..r });
    }
}

/// int_mail.cpp mail_DeleteAttach.
fn mail_delete_attach(c: &Ctx, mail_id: i32) -> bool {
    if let Some(r) = c.db().mail().id().find(mail_id) {
        c.db().mail().id().update(MailRow { attachments: Vec::new(), ..r });
    }
    true
}

/// int_mail.cpp mapif_Mail_getattach: the zeny and / or items of a mail leave the table and go to the map server.
fn mapif_mail_getattach(c: &Ctx, char_id: u32, mail_id: i32, mut kind: u8) {
    if kind & MAIL_ATT_ALL == 0 {
        return;
    }
    let Some(msg) = mail_loadmessage(c, mail_id) else { return };
    if msg.dest_id != char_id {
        return;
    }
    if !MAIL_RETRIEVE && msg.status != MAIL_READ {
        return;
    }
    if kind & MAIL_ATT_ZENY != 0 {
        if msg.zeny > 0 {
            if let Some(r) = c.db().mail().id().find(mail_id) {
                c.db().mail().id().update(MailRow { zeny: 0, ..r });
            }
        } else {
            kind &= !MAIL_ATT_ZENY;
        }
    }
    if kind & MAIL_ATT_ITEM != 0 {
        // No item was found
        if !msg.item.iter().any(|it| it.nameid > 0 && it.amount > 0) {
            kind &= !MAIL_ATT_ITEM;
        } else if !mail_delete_attach(c, mail_id) {
            return;
        }
    }
    if kind == MAIL_ATT_NONE {
        return; // No Attachment
    }
    let zeny = if kind & MAIL_ATT_ZENY != 0 { msg.zeny as i32 } else { 0 };
    let items = if kind & MAIL_ATT_ITEM != 0 { msg.item.clone() } else { vec![Item::new(0); MAIL_MAX_ITEM] };
    to_map(MailInter::GetAttachAck { char_id, mail_id, zeny, items });
}

/// int_mail.cpp mapif_Mail_delete. `from_map` is rAthena's `fd > 0` (a map server's request); otherwise the char
/// server triggered it (the timers) and the map server of `account_id` is told if that account is online.
fn mapif_mail_delete(c: &Ctx, from_map: bool, char_id: u32, mail_id: i32, account_id: u32) -> bool {
    // The Foreign Key will automatically delete all attachments (the DELETE of a missing row is not an error)
    c.db().mail().id().delete(mail_id);
    let failed = false;
    if !from_map {
        // Check for online players
        if crate::pc::get(c, account_id).is_none() {
            // The request was triggered inside the character server or the player is offline now
            return !failed;
        }
    }
    to_map(MailInter::DeleteAck { char_id, mail_id, failed });
    !failed
}

/// int_mail.cpp mapif_Mail_new: every map server hears about the new mail (nothing for a mail that was not saved).
pub fn mapif_mail_new(msg: &MailMessage) {
    if msg.id == 0 {
        return;
    }
    to_map(MailInter::New {
        dest_id: msg.dest_id,
        mail_id: msg.id,
        send_name: strncpy(&msg.send_name, NAME_LENGTH + 1),
        title: strncpy(&msg.title, MAIL_TITLE_LENGTH + 1),
        kind: msg.kind,
    });
}

/// int_mail.cpp mapif_Mail_return: the mail goes back to its sender as a new "RE:" mail of the returned tab.
fn mapif_mail_return(c: &Ctx, from_map: bool, char_id: u32, mail_id: i32, account_id_receiver: u32, account_id_sender: u32) {
    let Some(mut msg) = mail_loadmessage(c, mail_id) else { return };
    if msg.dest_id != char_id {
        return;
    }
    if !mapif_mail_delete(c, false, char_id, mail_id, account_id_receiver) {
        // Stop processing to not duplicate the mail
        return;
    }
    // If it was sent by the server we do not want to return the mail
    if msg.send_id == 0 {
        return;
    }
    // If we do not want to return mails without any attachments and the request was not sent by a user
    if !from_map && !MAIL_RETURN_EMPTY && !msg.item.iter().any(|it| it.nameid > 0) && msg.zeny == 0 {
        return;
    }
    // swap sender and receiver
    std::mem::swap(&mut msg.send_id, &mut msg.dest_id);
    let temp = strncpy(&msg.send_name, NAME_LENGTH);
    msg.send_name = strncpy(&msg.dest_name, NAME_LENGTH);
    msg.dest_name = strncpy(&temp, NAME_LENGTH);
    // set reply message title: snprintf("RE:%s") into char temp_[MAIL_TITLE_LENGTH + 3], then
    // safestrncpy(msg.title, temp_, sizeof(temp_)) — 43 bytes written into the 40-byte title, so the first three bytes
    // of the body that follows it in struct mail_message are overwritten too (the title's characters past 40, then
    // NUL padding; safestrncpy always writes all n bytes): the returned mail's body keeps at most two characters.
    let temp: Vec<char> = format!("RE:{}", msg.title).chars().take(MAIL_TITLE_LENGTH + 2).collect();
    msg.title = temp.iter().take(MAIL_TITLE_LENGTH).collect();
    msg.body = temp.iter().skip(MAIL_TITLE_LENGTH).collect();
    msg.status = MAIL_NEW;
    msg.kind = MAIL_INBOX_RETURNED;
    msg.timestamp = now(c);
    let new_mail = mail_savemessage(c, &mut msg);
    mapif_mail_new(&msg);
    if !from_map {
        // Check for online players
        if crate::pc::get(c, account_id_sender).is_none() {
            return;
        }
    }
    to_map(MailInter::ReturnAck { char_id, mail_id, fail: new_mail == 0 });
}

/// int_mail.cpp mapif_parse_Mail_send: resolves the receiver (by char id, then by name: in RODEX you can even send
/// mails to yourself), saves the mail, tells the sender and every map server.
fn mapif_parse_mail_send(c: &Ctx, _account_id: u32, mut msg: MailMessage) {
    if msg.dest_id != 0 {
        let found = c.db().character().id().find(msg.dest_id);
        msg.dest_id = 0;
        msg.dest_name = String::new();
        if let Some(ch) = found {
            msg.dest_id = ch.id;
            msg.dest_name = strncpy(&ch.name, NAME_LENGTH);
        }
    }
    // Try to find the Dest Char by Name
    let dn = strncpy(&msg.dest_name, NAME_LENGTH + 1).to_lowercase();
    if let Some(ch) = c.db().character().name_key().find(&dn) {
        msg.dest_id = ch.id;
    }
    msg.status = MAIL_NEW;
    if msg.dest_id > 0 {
        msg.id = mail_savemessage(c, &mut msg);
    }
    to_map(MailInter::SendAck { msg: msg.clone() }); // notify sender
    mapif_mail_new(&msg); // notify recipient
}

/// int_mail.cpp mail_sendmail: a mail from the char server itself (int_achievement.cpp's reward mails). Returns
/// false when it could not be stored.
#[allow(clippy::too_many_arguments)]
pub fn mail_sendmail(c: &Ctx, send_id: u32, send_name: &str, dest_id: u32, dest_name: &str, title: &str, body: &str, zeny: i32, item: &[Item]) -> bool {
    ensure_timers(c);
    let mut msg = MailMessage::new();
    msg.send_id = send_id;
    msg.send_name = strncpy(send_name, NAME_LENGTH);
    msg.dest_id = dest_id;
    msg.dest_name = strncpy(dest_name, NAME_LENGTH);
    msg.title = strncpy(title, MAIL_TITLE_LENGTH);
    msg.body = strncpy(body, MAIL_BODY_LENGTH);
    msg.zeny = zeny as u32;
    for (i, it) in item.iter().take(MAIL_MAX_ITEM).enumerate() {
        msg.item[i] = it.clone();
    }
    msg.timestamp = now(c);
    msg.kind = MAIL_INBOX_NORMAL;
    if mail_savemessage(c, &mut msg) == 0 {
        return false;
    }
    mapif_mail_new(&msg);
    true
}

/// int_mail.cpp mapif_parse_Mail_receiver_check / mapif_Mail_receiver_send: char id, class and level of a name
/// (zeros when there is no such character).
fn mapif_parse_mail_receiver_check(c: &Ctx, requesting_char_id: u32, name: &str) {
    let name = strncpy(name, NAME_LENGTH);
    let (mut char_id, mut class, mut base_level) = (0u32, 0u16, 0u16);
    if let Some(ch) = c.db().character().name_key().find(&name.to_lowercase()) {
        char_id = ch.id;
        class = ch.class;
        base_level = ch.base_level;
    }
    to_map(MailInter::Receiver { requesting_char_id, char_id, class, base_level, name });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn strncpy_keeps_n_minus_one() {
        assert_eq!(strncpy("abcdef", 4), "abc");
        assert_eq!(strncpy("ab", 4), "ab");
    }

    #[test]
    fn empty_message_has_five_slots() {
        let m = MailMessage::new();
        assert_eq!(m.item.len(), MAIL_MAX_ITEM);
        assert_eq!(MailData::default().msg.len(), MAIL_MAX_INBOX);
    }
}
