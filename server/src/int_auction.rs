//! char/int_auction.cpp: the char server's half of the auction system (the `auction` SQL table, the in-memory
//! `auction_db`, the end timer, the mails on end / cancel / close / outbid / buy-now, and the inter-server messages
//! 0x3050-0x3055 / 0x3850-0x3855), inside the module.
//!
//! rAthena keeps every auction twice: a row of the `auction` table and a `struct auction_data` in
//! `auction_db` (std::unordered_map<uint32, std::shared_ptr<auction_data>>, loaded by inter_auctions_fromsql when the
//! char server starts). Here both are one table ([`AuctionData`]: the struct as the char server holds it in memory,
//! the SQL columns included), and the inter messages go through intif.rs's queue ([`AuctionInter`], one
//! `Inter::Auction` variant), handled after the map-server function that sent them, exactly as the two processes see
//! them on their one TCP link. The map server's half is auction.rs.
//!
//! Deliberate differences (no SQL, one map server, a server that does not restart):
//! - auction ids come from the module's id counter (`ids::next("auction_id", 1)`), as the MyISAM table's
//!   AUTO_INCREMENT does (sql-files/main.sql:54-91; an id is never handed out twice);
//! - mapif_parse_Auction_requestlist walks auction_db in the unordered_map's iteration order (five results per page,
//!   so the order decides which auctions a page shows): [`AuctionMem`] keeps that order ([`Umap32`], libstdc++'s hash
//!   table as the oracle's g++ 13.3.0 builds it);
//! - "the char server starts" (char.cpp do_init → inter_init_sql → inter_auction_sql_init → inter_auctions_fromsql)
//!   is "the module has no [`AuctionMem`] row": [`ensure_loaded`] then runs inter_auctions_fromsql (a new map filled
//!   in `auction_id DESC` order, every auction's end timer armed again from its `timestamp`, the items reduced to what
//!   the SQL table stores). That happens once on a new database; the timers and the map order then live on in their
//!   tables, as the char server's memory does while it runs. (The parity rig deletes the row to get what an rAthena
//!   char-server restart does.)

use spacetimedb::{table, Table};

use crate::db::items::{IT_ARMOR, IT_CARD, IT_ETC, IT_PETARMOR, IT_WEAPON};
use crate::int_mail::{mail_sendmail, strncpy, NAME_LENGTH};
use crate::intif::{self, Inter};
use crate::inventory::Item;
use crate::timer::{self, Func};
use crate::world::Ctx;

/// mmo.hpp ITEM_NAME_LENGTH.
pub const ITEM_NAME_LENGTH: usize = 50;

/// conf/msg_conf/char_msg.conf 200-211 (the char server's msg_txt; Loki's imports change none of them).
pub const MSG_AUCTION_MANAGER: &str = "Auction Manager"; // 200
pub const MSG_AUCTION: &str = "Auction"; // 201
pub const MSG_WON: &str = "Thanks, you won the auction!."; // 202
pub const MSG_PAYMENT: &str = "Payment for your auction!."; // 203
pub const MSG_NO_BUYERS: &str = "No buyers have been found for your auction."; // 204
pub const MSG_CANCELED: &str = "Auction canceled."; // 205
pub const MSG_CLOSED: &str = "Auction closed."; // 206
pub const MSG_WINNER: &str = "Auction winner."; // 207
pub const MSG_HIGHER_BID: &str = "Someone has placed a higher bid."; // 208
pub const MSG_OWN_HIGHER_BID: &str = "You have placed a higher bid."; // 209
pub const MSG_BUYNOW_WON: &str = "You have won the auction."; // 210
pub const MSG_BUYNOW_PAYMENT: &str = "Payment for your auction!."; // 211

/// mmo.hpp struct auction_data (map and char server) and the rAthena `auction` table (sql-files/main.sql:54-91).
/// Names are latin-1 code points, as every name in the module. `item` is the whole `struct item` the char server
/// holds in memory (the map server's copy of the inventory slot; the SQL table stores only nameid, refine, attribute,
/// unique_id, enchantgrade, the cards and the options: see [`sql_item`]).
#[table(accessor = auction)]
#[derive(Clone, Debug, PartialEq)]
pub struct AuctionData {
    #[primary_key]
    pub auction_id: u32,
    pub seller_id: u32,
    pub seller_name: String,
    pub buyer_id: u32,
    pub buyer_name: String,
    pub item: Item,
    /// This data is required for searching, as itemdb is not read by char server.
    pub item_name: String,
    /// `int16 type` (the item's type).
    pub kind: i16,
    pub hours: u16,
    pub price: i32,
    pub buynow: i32,
    /// auction's end time (time_t).
    pub timestamp: i64,
    /// `int32 auction_end_timer` (0 = INVALID_TIMER).
    pub auction_end_timer: u64,
}

impl Default for AuctionData {
    /// `struct auction_data auction` zeroed.
    fn default() -> Self {
        AuctionData {
            auction_id: 0,
            seller_id: 0,
            seller_name: String::new(),
            buyer_id: 0,
            buyer_name: String::new(),
            item: Item::new(0),
            item_name: String::new(),
            kind: 0,
            hours: 0,
            price: 0,
            buynow: 0,
            timestamp: 0,
            auction_end_timer: 0,
        }
    }
}

/// The char server's memory: `auction_db`'s iteration order (one row, key 0). No row: the char server has not loaded
/// the auctions yet (see the module doc).
#[table(accessor = auction_mem)]
#[derive(Clone, Debug)]
pub struct AuctionMem {
    #[primary_key]
    pub key: u8,
    /// The auction ids in the unordered_map's list order.
    pub order: Vec<u32>,
    pub buckets: u64,
    pub next_resize: u64,
}

/// The auction messages on the inter link (intif.cpp / int_auction.cpp packet ids in the docs).
#[derive(Clone, Debug)]
pub enum AuctionInter {
    // ---- map → char (inter_auction_parse_frommap)
    /// 0x3050 intif_Auction_requestlist (`page` as RFIFOW reads it).
    RequestList { char_id: u32, kind: i16, price: i32, searchtext: String, page: u16 },
    /// 0x3051 intif_Auction_register.
    Register { auction: AuctionData },
    /// 0x3052 intif_Auction_cancel.
    Cancel { char_id: u32, auction_id: u32 },
    /// 0x3053 intif_Auction_close.
    Close { char_id: u32, auction_id: u32 },
    /// 0x3055 intif_Auction_bid.
    Bid { char_id: u32, auction_id: u32, bid: i32, name: String },
    // ---- char → map (intif_parse)
    /// 0x3850 mapif_Auction_sendlist.
    Results { char_id: u32, count: i16, pages: i16, auctions: Vec<AuctionData> },
    /// 0x3851 mapif_Auction_register.
    RegisterAck { auction: AuctionData },
    /// 0x3852 mapif_Auction_cancel.
    CancelAck { char_id: u32, result: u8 },
    /// 0x3853 mapif_Auction_close.
    CloseAck { char_id: u32, result: u8 },
    /// 0x3854 mapif_Auction_message (to every map server).
    Message { char_id: u32, result: u8 },
    /// 0x3855 mapif_Auction_bid.
    BidAck { char_id: u32, bid: i32, result: u8 },
}

/// intif.rs's dispatch for [`Inter::Auction`]: char/int_auction.cpp inter_auction_parse_frommap for requests,
/// map/intif.cpp intif_parse for answers.
pub fn dispatch(c: &Ctx, m: AuctionInter) {
    match m {
        AuctionInter::RequestList { char_id, kind, price, searchtext, page } => mapif_parse_auction_requestlist(c, char_id, kind, price, &searchtext, page),
        AuctionInter::Register { auction } => mapif_parse_auction_register(c, auction),
        AuctionInter::Cancel { char_id, auction_id } => mapif_parse_auction_cancel(c, char_id, auction_id),
        AuctionInter::Close { char_id, auction_id } => mapif_parse_auction_close(c, char_id, auction_id),
        AuctionInter::Bid { char_id, auction_id, bid, name } => mapif_parse_auction_bid(c, char_id, auction_id, bid, &name),
        AuctionInter::Results { char_id, count, pages, auctions } => crate::auction::intif_parse_auction_results(c, char_id, count, pages, &auctions),
        AuctionInter::RegisterAck { auction } => crate::auction::intif_parse_auction_register(c, auction),
        AuctionInter::CancelAck { char_id, result } => crate::auction::intif_parse_auction_cancel(c, char_id, result),
        AuctionInter::CloseAck { char_id, result } => crate::auction::intif_parse_auction_close(c, char_id, result),
        AuctionInter::Message { char_id, result } => crate::auction::intif_parse_auction_message(c, char_id, result),
        AuctionInter::BidAck { char_id, bid, result } => crate::auction::intif_parse_auction_bid(c, char_id, bid, result),
    }
}

fn to_map(m: AuctionInter) {
    intif::send(Inter::Auction(m));
}

/// time(nullptr).
fn now(c: &Ctx) -> i64 {
    c.tick() / 1000
}

// ---------------------------------------------------------------------------------------------- auction_db's order

/// The bucket counts of a libstdc++ `std::unordered_map` that grows by single inserts (`_Prime_rehash_policy`,
/// max_load_factor 1.0): 13 at the first insert (the policy's initial hint of 11), then the first prime of
/// `__prime_list` not below twice the count whenever the element count would pass the bucket count. Printed by the
/// oracle's compiler (g++ 13.3.0, Ubuntu 13.3.0-6ubuntu2~24.04.1) for 3,000,000 inserts.
const BUCKET_CHAIN: [u64; 19] =
    [13, 29, 59, 127, 257, 541, 1109, 2357, 5087, 10273, 20753, 42043, 85229, 172933, 351061, 712697, 1447153, 2938679, 5967347];

/// The iteration order of rAthena's `auction_db` (std::unordered_map<uint32, std::shared_ptr<auction_data>>,
/// int_auction.cpp:29), as skill/umap.rs keeps `sd->scd`'s: libstdc++'s unordered_map is a hash table over one singly
/// linked list of all nodes; std::hash<uint32> is the identity and the bucket is `key % bucket_count`; a new key goes
/// first in its bucket's group of nodes, or at the front of the list when its bucket is empty
/// (`_Hashtable::_M_insert_bucket_begin`); before an insert `_Prime_rehash_policy::_M_need_rehash` may grow the bucket
/// count ([`BUCKET_CHAIN`]) and `_M_rehash_aux` then walks the old list in order, putting each node first in its new
/// bucket's group or at the front; erase unlinks the node and never shrinks the table.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Umap32 {
    pub order: Vec<u32>,
    /// 1 for a new map.
    pub buckets: u64,
    /// `_M_next_resize` (0 for a new map).
    pub next_resize: u64,
}

fn bucket(key: u32, buckets: u64) -> u64 {
    key as u64 % buckets.max(1)
}

/// `_M_insert_bucket_begin`: `key` goes before the first node of its bucket, or at the front of the list.
fn insert_bucket_begin(order: &mut Vec<u32>, key: u32, buckets: u64) {
    let b = bucket(key, buckets);
    match order.iter().position(|&k| bucket(k, buckets) == b) {
        Some(i) => order.insert(i, key),
        None => order.insert(0, key),
    }
}

impl Umap32 {
    /// `map[key] = …` (operator[]): a key already there keeps its place; a new one is inserted after the rehash check.
    pub fn insert(&mut self, key: u32) {
        if self.order.contains(&key) {
            return;
        }
        let n_elt = self.order.len() as u64;
        let n_bkt = self.buckets.max(1);
        // _M_need_rehash(n_bkt, n_elt, 1)
        if n_elt + 1 > self.next_resize {
            let min_bkts = (n_elt + 1).max(if self.next_resize != 0 { 0 } else { 11 });
            if min_bkts >= n_bkt {
                // _M_next_bkt(max(floor(min_bkts) + 1, n_bkt * 2))
                let want = (min_bkts + 1).max(n_bkt * 2);
                let nb = BUCKET_CHAIN.iter().copied().find(|&p| p >= want).unwrap_or(BUCKET_CHAIN[BUCKET_CHAIN.len() - 1]);
                // _M_rehash_aux: the old list in order, each node first in its new bucket's group
                let old = std::mem::take(&mut self.order);
                for k in old {
                    insert_bucket_begin(&mut self.order, k, nb);
                }
                self.buckets = nb;
                self.next_resize = nb;
            } else {
                self.next_resize = n_bkt;
            }
        }
        let nb = self.buckets.max(1);
        insert_bucket_begin(&mut self.order, key, nb);
    }

    /// `map.erase(key)`.
    pub fn erase(&mut self, key: u32) {
        self.order.retain(|&k| k != key);
    }
}

fn mem(c: &Ctx) -> Umap32 {
    c.db().auction_mem().key().find(0).map(|m| Umap32 { order: m.order, buckets: m.buckets, next_resize: m.next_resize }).unwrap_or_default()
}

fn put_mem(c: &Ctx, m: &Umap32) {
    let row = AuctionMem { key: 0, order: m.order.clone(), buckets: m.buckets, next_resize: m.next_resize };
    if c.db().auction_mem().key().find(0).is_some() {
        c.db().auction_mem().key().update(row);
    } else {
        c.db().auction_mem().insert(row);
    }
}

/// `util::umap_find( auction_db, auction_id )`.
fn auction_find(c: &Ctx, auction_id: u32) -> Option<AuctionData> {
    c.db().auction().auction_id().find(auction_id)
}

/// Every auction in auction_db's iteration order.
fn auction_db(c: &Ctx) -> Vec<AuctionData> {
    mem(c).order.iter().filter_map(|&id| auction_find(c, id)).collect()
}

// ---------------------------------------------------------------------------------------------- int_auction.cpp

/// int_auction.cpp auction_count: the auctions a character sells (`buy` false) or leads as the highest bidder.
pub fn auction_count(c: &Ctx, char_id: u32, buy: bool) -> i32 {
    c.db().auction().iter().filter(|a| (buy && a.buyer_id == char_id) || (!buy && a.seller_id == char_id)).count() as i32
}

/// int_auction.cpp auction_save: the UPDATE of the auction's row (the struct and the row are one here).
fn auction_save(c: &Ctx, auction: &AuctionData) {
    c.db().auction().auction_id().update(auction.clone());
}

/// The columns of the `auction` table an item keeps (nameid, refine, attribute, unique_id, enchantgrade, card0-3,
/// option_id/val/parm 0-4), with what inter_auctions_fromsql sets (int_auction.cpp:218-245): identify 1, amount 1,
/// expire_time 0, everything else of the make_shared'd (zeroed) struct 0.
fn sql_item(it: &Item) -> Item {
    let mut o = Item::new(it.nameid);
    o.refine = it.refine;
    o.attribute = it.attribute;
    o.unique_id = it.unique_id;
    o.enchantgrade = it.enchantgrade;
    o.card0 = it.card0;
    o.card1 = it.card1;
    o.card2 = it.card2;
    o.card3 = it.card3;
    o.option = it.options().to_vec();
    o.identify = 1;
    o.amount = 1;
    o.expire_time = 0;
    o
}

/// Whether auction_create's INSERT succeeds. The module has no SQL, but rAthena's INSERT can fail, and then the
/// auction is not created (auction_id 0: the seller gets the item and the fee back, as for a sixth auction): MariaDB's
/// default sql_mode (STRICT_TRANS_TABLES; the reference server's) refuses a single-row INSERT whose value does not fit
/// its column ("Out of range value for column 'price'"). The columns that values a client can cause do not fit
/// (sql-files/main.sql:54-91): `price` and `buynow` are `int unsigned` and are written with '%d' from the int32
/// fields (clif_parse_Auction_register takes the client's uint32 as int32 and only requires price < buynow, so a
/// price of 0x80000000 or more is negative); `refine` and `attribute` are `tinyint unsigned` written with '%d' from
/// `char` fields (signed on the reference build). No failed INSERT consumes an auction id (MyISAM AUTO_INCREMENT).
fn insert_fits(auction: &AuctionData) -> bool {
    auction.price >= 0 && auction.buynow >= 0 && (auction.item.refine as i8) >= 0 && (auction.item.attribute as i8) >= 0
}

/// int_auction.cpp auction_create: the end time, the INSERT (the id; 0 when it fails: [`insert_fits`]), then the
/// item's amount / identify / expire_time, the end timer and auction_db. Returns the auction id.
fn auction_create(c: &Ctx, auction: &mut AuctionData) -> u32 {
    auction.timestamp = now(c) + (auction.hours as i64 * 3600);
    // the INSERT: seller_name / buyer_name bound with strnlen(…, NAME_LENGTH), item_name with ITEM_NAME_LENGTH; the
    // map server wrote all three with safestrncpy, so they are whole
    if !insert_fits(auction) {
        // SqlStmt_ShowDebug(stmt); auction->auction_id = 0; (int_auction.cpp:114-117)
        log::error!("auction_create: DB error - Out of range value (price {}, buynow {}, refine {}, attribute {})", auction.price, auction.buynow, auction.item.refine, auction.item.attribute);
        auction.auction_id = 0;
        return 0;
    }
    let tick = auction.hours as i64 * 3_600_000;
    auction.item.amount = 1;
    auction.item.identify = 1;
    auction.item.expire_time = 0;
    auction.auction_id = crate::ids::next(c.rc, "auction_id", 1); // stmt.LastInsertId()
    auction.auction_end_timer = timer::add(c, c.tick() + tick, Func::AuctionEndTimer, auction.auction_id, 0);
    log::info!("New Auction {} | time left {} ms | By {}.", auction.auction_id, tick, auction.seller_name);
    c.db().auction().insert(auction.clone());
    let mut m = mem(c);
    m.insert(auction.auction_id); // auction_db[auction->auction_id] = auction
    put_mem(c, &m);
    auction.auction_id
}

/// int_auction.cpp mapif_Auction_message (0x3854, chmapif_sendall).
fn mapif_auction_message(char_id: u32, result: u8) {
    to_map(AuctionInter::Message { char_id, result });
}

/// int_auction.cpp auction_end_timer (timer id = the auction id): with a bidder the item goes to the buyer and the
/// price to the seller, without one the item goes back to the seller; the auction is deleted.
pub fn auction_end_timer(c: &Ctx, auction_id: u32) {
    let Some(mut auction) = auction_find(c, auction_id) else { return };
    if auction.buyer_id != 0 {
        mail_sendmail(c, 0, MSG_AUCTION_MANAGER, auction.buyer_id, &auction.buyer_name, MSG_AUCTION, MSG_WON, 0, std::slice::from_ref(&auction.item));
        mapif_auction_message(auction.buyer_id, 6); // You have won the auction
        mail_sendmail(c, 0, MSG_AUCTION_MANAGER, auction.seller_id, &auction.seller_name, MSG_AUCTION, MSG_PAYMENT, auction.price, &[]);
    } else {
        mail_sendmail(c, 0, MSG_AUCTION_MANAGER, auction.seller_id, &auction.seller_name, MSG_AUCTION, MSG_NO_BUYERS, 0, std::slice::from_ref(&auction.item));
    }
    log::info!("Auction End: id {}.", auction.auction_id);
    auction.auction_end_timer = 0; // INVALID_TIMER
    auction_delete(c, &auction);
}

/// int_auction.cpp auction_delete: the row, the end timer (when it has one), auction_db.
fn auction_delete(c: &Ctx, auction: &AuctionData) {
    let auction_id = auction.auction_id;
    c.db().auction().auction_id().delete(auction_id);
    if auction.auction_end_timer != 0 {
        timer::delete(c, auction.auction_end_timer);
    }
    let mut m = mem(c);
    m.erase(auction_id);
    put_mem(c, &m);
}

/// int_auction.cpp inter_auctions_fromsql: every row of the table, newest first, becomes an auction_db entry with a
/// new end timer: at its end time, or 10 seconds from now when that has passed ("10 Second's to process ended
/// auctions").
pub fn inter_auctions_fromsql(c: &Ctx) {
    let tick = c.tick();
    let now = now(c);
    // SELECT … FROM `auction` ORDER BY `auction_id` DESC
    let mut rows: Vec<AuctionData> = c.db().auction().iter().collect();
    rows.sort_by(|a, b| b.auction_id.cmp(&a.auction_id));
    let mut m = Umap32::default();
    for mut auction in rows {
        // the char server that kept this auction is gone, and its timer with it
        if auction.auction_end_timer != 0 {
            timer::delete(c, auction.auction_end_timer);
        }
        auction.seller_name = strncpy(&auction.seller_name, NAME_LENGTH);
        auction.buyer_name = strncpy(&auction.buyer_name, NAME_LENGTH);
        auction.item_name = strncpy(&auction.item_name, ITEM_NAME_LENGTH);
        auction.item = sql_item(&auction.item);
        let endtick = if auction.timestamp > now {
            // ((uint32)(auction->timestamp - now) * 1000) + tick: the product is a uint32
            ((auction.timestamp - now) as u32).wrapping_mul(1000) as i64 + tick
        } else {
            tick + 10000 // 10 Second's to process ended auctions
        };
        auction.auction_end_timer = timer::add(c, endtick, Func::AuctionEndTimer, auction.auction_id, 0);
        m.insert(auction.auction_id);
        c.db().auction().auction_id().update(auction);
    }
    put_mem(c, &m);
}

/// inter.cpp inter_init_sql → inter_auction_sql_init, when the module has not loaded the auctions yet (see the module
/// doc). Called where the module arms the char server's other start-up timers (int_mail.rs ensure_timers) and before
/// every request.
pub fn ensure_loaded(c: &Ctx) {
    if c.db().auction_mem().key().find(0).is_none() {
        inter_auctions_fromsql(c);
    }
}

/// int_auction.cpp mapif_parse_Auction_requestlist's loop over auction_db (`auctions` in its iteration order): the
/// auctions that match the search type, counted five to a page; the entries of the requested page and the number of
/// pages. Types: 0 armor (and pet armor), 1 weapon, 2 card, 3 misc, 4 the item name contains the text (strstr), 5 the
/// price is at most `price`, 6 sold by the character, 7 led by the character; any other type matches everything.
/// `page` is the packet's uint16 (`int16 page = max(1, RFIFOW(fd,14))`: 0 is page 1, 0x8000 and above are negative
/// and match no page).
fn requestlist(auctions: &[AuctionData], char_id: u32, kind: i16, price: i32, searchtext: &str, page: u16) -> (Vec<AuctionData>, i16) {
    let page = std::cmp::max(1, page as i32) as i16;
    let mut buf: Vec<AuctionData> = Vec::new();
    let (mut i, mut pages) = (0i16, 1i16);
    for auction in auctions {
        if (kind == 0 && auction.kind != IT_ARMOR as i16 && auction.kind != IT_PETARMOR as i16)
            || (kind == 1 && auction.kind != IT_WEAPON as i16)
            || (kind == 2 && auction.kind != IT_CARD as i16)
            || (kind == 3 && auction.kind != IT_ETC as i16)
            || (kind == 4 && !auction.item_name.contains(searchtext)) // strstr
            || (kind == 5 && auction.price > price)
            || (kind == 6 && auction.seller_id != char_id)
            || (kind == 7 && auction.buyer_id != char_id)
        {
            continue;
        }
        i += 1;
        if i > 5 {
            // Counting Pages of Total Results (5 Results per Page)
            pages = pages.wrapping_add(1);
            i = 1; // First Result of This Page
        }
        if page != pages {
            continue; // This is not the requested Page
        }
        buf.push(auction.clone()); // Found Results
    }
    (buf, pages)
}

/// int_auction.cpp mapif_parse_Auction_requestlist (0x3050) / mapif_Auction_sendlist (0x3850).
fn mapif_parse_auction_requestlist(c: &Ctx, char_id: u32, kind: i16, price: i32, searchtext: &str, page: u16) {
    ensure_loaded(c);
    let (buf, pages) = requestlist(&auction_db(c), char_id, kind, price, searchtext, page);
    to_map(AuctionInter::Results { char_id, count: buf.len() as i16, pages, auctions: buf });
}

/// int_auction.cpp mapif_parse_Auction_register (0x3051) / mapif_Auction_register (0x3851): a seller's sixth auction
/// goes back as it came (auction id 0).
fn mapif_parse_auction_register(c: &Ctx, mut auction: AuctionData) {
    ensure_loaded(c);
    if auction_count(c, auction.seller_id, false) < 5 {
        auction.auction_id = auction_create(c, &mut auction);
    }
    to_map(AuctionInter::RegisterAck { auction });
}

/// int_auction.cpp mapif_parse_Auction_cancel (0x3052) / mapif_Auction_cancel (0x3852).
fn mapif_parse_auction_cancel(c: &Ctx, char_id: u32, auction_id: u32) {
    ensure_loaded(c);
    let Some(auction) = auction_find(c, auction_id) else {
        to_map(AuctionInter::CancelAck { char_id, result: 1 }); // Bid Number is Incorrect
        return;
    };
    if auction.seller_id != char_id {
        to_map(AuctionInter::CancelAck { char_id, result: 2 }); // You cannot end the auction
        return;
    }
    if auction.buyer_id > 0 {
        to_map(AuctionInter::CancelAck { char_id, result: 3 }); // An auction with at least one bidder cannot be canceled
        return;
    }
    mail_sendmail(c, 0, MSG_AUCTION_MANAGER, auction.seller_id, &auction.seller_name, MSG_AUCTION, MSG_CANCELED, 0, std::slice::from_ref(&auction.item));
    auction_delete(c, &auction);
    to_map(AuctionInter::CancelAck { char_id, result: 0 }); // The auction has been canceled
}

/// int_auction.cpp mapif_parse_Auction_close (0x3053) / mapif_Auction_close (0x3853).
fn mapif_parse_auction_close(c: &Ctx, char_id: u32, auction_id: u32) {
    ensure_loaded(c);
    let Some(auction) = auction_find(c, auction_id) else {
        to_map(AuctionInter::CloseAck { char_id, result: 2 }); // Bid Number is Incorrect
        return;
    };
    if auction.seller_id != char_id {
        to_map(AuctionInter::CloseAck { char_id, result: 1 }); // You cannot end the auction
        return;
    }
    if auction.buyer_id == 0 {
        to_map(AuctionInter::CloseAck { char_id, result: 1 }); // You cannot end the auction
        return;
    }
    // Send Money to Seller
    mail_sendmail(c, 0, MSG_AUCTION_MANAGER, auction.seller_id, &auction.seller_name, MSG_AUCTION, MSG_CLOSED, auction.price, &[]);
    // Send Item to Buyer
    mail_sendmail(c, 0, MSG_AUCTION_MANAGER, auction.buyer_id, &auction.buyer_name, MSG_AUCTION, MSG_WINNER, 0, std::slice::from_ref(&auction.item));
    mapif_auction_message(auction.buyer_id, 6); // You have won the auction
    auction_delete(c, &auction);
    to_map(AuctionInter::CloseAck { char_id, result: 0 }); // You have ended the auction
}

/// int_auction.cpp mapif_parse_Auction_bid (0x3055) / mapif_Auction_bid (0x3855; `bid` is the zeny to give back).
fn mapif_parse_auction_bid(c: &Ctx, char_id: u32, auction_id: u32, bid: i32, name: &str) {
    ensure_loaded(c);
    let auction = auction_find(c, auction_id).filter(|a| !(a.price >= bid || a.seller_id == char_id));
    let Some(mut auction) = auction else {
        to_map(AuctionInter::BidAck { char_id, bid, result: 0 }); // You have failed to bid in the auction
        return;
    };
    if auction_count(c, char_id, true) > 4 && bid < auction.buynow && auction.buyer_id != char_id {
        to_map(AuctionInter::BidAck { char_id, bid, result: 9 }); // You cannot place more than 5 bids at a time
        return;
    }
    if auction.buyer_id > 0 {
        // Send Money back to the previous Buyer
        if auction.buyer_id != char_id {
            mail_sendmail(c, 0, MSG_AUCTION_MANAGER, auction.buyer_id, &auction.buyer_name, MSG_AUCTION, MSG_HIGHER_BID, auction.price, &[]);
            mapif_auction_message(auction.buyer_id, 7); // You have failed to win the auction
        } else {
            mail_sendmail(c, 0, MSG_AUCTION_MANAGER, auction.buyer_id, &auction.buyer_name, MSG_AUCTION, MSG_OWN_HIGHER_BID, auction.price, &[]);
        }
    }
    auction.buyer_id = char_id;
    auction.buyer_name = strncpy(name, NAME_LENGTH);
    auction.price = bid;
    if bid >= auction.buynow {
        // Automatic won the auction
        to_map(AuctionInter::BidAck { char_id, bid: bid - auction.buynow, result: 1 }); // You have successfully bid in the auction
        mail_sendmail(c, 0, MSG_AUCTION_MANAGER, auction.buyer_id, &auction.buyer_name, MSG_AUCTION, MSG_BUYNOW_WON, 0, std::slice::from_ref(&auction.item));
        mapif_auction_message(char_id, 6); // You have won the auction
        mail_sendmail(c, 0, MSG_AUCTION_MANAGER, auction.seller_id, &auction.seller_name, MSG_AUCTION, MSG_BUYNOW_PAYMENT, auction.buynow, &[]);
        auction_delete(c, &auction);
        return;
    }
    auction_save(c, &auction);
    to_map(AuctionInter::BidAck { char_id, bid: 0, result: 1 }); // You have successfully bid in the auction
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Replays `+K` (insert), `-K` (erase) and checks each `p` against the expected (buckets, order).
    fn replay(ops: &str, expected: &[(u64, &[u32])]) {
        let mut m = Umap32::default();
        let mut e = expected.iter();
        for op in ops.split_whitespace() {
            match op.as_bytes()[0] {
                b'+' => m.insert(op[1..].parse().unwrap_or(0)),
                b'-' => m.erase(op[1..].parse().unwrap_or(0)),
                _ => {
                    let (b, order) = e.next().copied().unwrap_or((0, &[]));
                    assert_eq!((m.buckets, m.order.as_slice()), (b, order), "after {op} in {ops}");
                }
            }
        }
        assert!(e.next().is_none(), "{ops}");
    }

    /// The orders printed by std::unordered_map<uint32_t, std::shared_ptr<int>> built with g++ 13.3.0 (the oracle's
    /// compiler; the probe is in server/ARCHITECTURE.md).
    #[test]
    fn umap32_matches_libstdcxx() {
        replay("+1 p +2 p +3 p +4 +5 +6 p", &[(13, &[1]), (13, &[2, 1]), (13, &[3, 2, 1]), (13, &[6, 5, 4, 3, 2, 1])]);
        replay(
            "+1 +2 +3 +4 +5 +6 +7 +8 +9 +10 +11 +12 +13 p +14 p +15 +16 p",
            &[
                (13, &[13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1]),
                (29, &[14, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]),
                (29, &[16, 15, 14, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]),
            ],
        );
        replay("+1 +2 +3 -2 p +4 p -1 -3 -4 p +5 p +18 p +31 p", &[(13, &[3, 1]), (13, &[4, 3, 1]), (13, &[]), (13, &[5]), (13, &[18, 5]), (13, &[31, 18, 5])]);
        replay("+7 +20 +33 +8 p -20 p +46 p", &[(13, &[8, 33, 20, 7]), (13, &[8, 33, 7]), (13, &[8, 46, 33, 7])]);
        // inter_auctions_fromsql: ORDER BY auction_id DESC into a new map
        replay("+9 +8 +6 +5 +3 +2 p", &[(13, &[2, 3, 5, 6, 8, 9])]);
        replay(
            "+40 +39 +38 +37 +36 +35 +34 +33 +32 +31 +30 +29 +28 +27 p +26 p",
            &[(29, &[27, 40, 39, 38, 37, 36, 35, 34, 33, 32, 31, 30, 29, 28]), (29, &[26, 27, 40, 39, 38, 37, 36, 35, 34, 33, 32, 31, 30, 29, 28])],
        );
    }

    /// The bucket counts follow the chain the oracle's compiler printed; erase never shrinks the table.
    #[test]
    fn umap32_bucket_chain() {
        let mut m = Umap32::default();
        let mut seen = vec![];
        for i in 1..=6000u32 {
            let before = m.buckets;
            m.insert(i);
            if m.buckets != before {
                seen.push((m.buckets, i));
            }
        }
        assert_eq!(seen, [(13, 1), (29, 14), (59, 30), (127, 60), (257, 128), (541, 258), (1109, 542), (2357, 1110), (5087, 2358), (10273, 5088)]);
        let mut m = Umap32::default();
        for i in 1..=20u32 {
            m.insert(i);
        }
        for i in 1..=18u32 {
            m.erase(i);
        }
        assert_eq!((m.buckets, m.order.as_slice()), (29, &[20u32, 19][..]));
        for k in [21u32, 50, 48, 77] {
            m.insert(k);
        }
        assert_eq!((m.buckets, m.order.as_slice()), (29, &[50u32, 21, 20, 77, 48, 19][..]));
    }

    fn auc(id: u32, seller: u32, buyer: u32, kind: u8, name: &str, price: i32) -> AuctionData {
        AuctionData { auction_id: id, seller_id: seller, buyer_id: buyer, kind: kind as i16, item_name: name.to_string(), price, buynow: price + 100, hours: 1, ..Default::default() }
    }

    /// mapif_parse_Auction_requestlist: the type filters, five results to a page, the page argument's quirks.
    #[test]
    fn requestlist_filters_and_pages() {
        let ids = |r: &(Vec<AuctionData>, i16)| (r.0.iter().map(|a| a.auction_id).collect::<Vec<_>>(), r.1);
        let mut db = Vec::new();
        for i in 1..=12u32 {
            let (kind, name) = match i % 4 {
                0 => (IT_ARMOR, "Cotton Shirt"),
                1 => (IT_WEAPON, "Knife"),
                2 => (IT_CARD, "Poring Card"),
                _ => (IT_ETC, "Jellopy"),
            };
            db.push(auc(i, 100 + i % 2, if i == 3 { 7 } else { 0 }, kind, name, i as i32 * 10));
        }
        db.push(auc(13, 100, 0, IT_PETARMOR, "Skull Helm", 5));
        db.push(auc(14, 100, 0, crate::db::items::IT_SHADOWGEAR, "Shadow Weapon", 6));
        // armor: the pet armor too; the shadow gear is in no type's list
        assert_eq!(ids(&requestlist(&db, 1, 0, 0, "", 1)), (vec![4, 8, 12, 13], 1));
        assert_eq!(ids(&requestlist(&db, 1, 1, 0, "", 1)), (vec![1, 5, 9], 1));
        assert_eq!(ids(&requestlist(&db, 1, 2, 0, "", 1)), (vec![2, 6, 10], 1));
        assert_eq!(ids(&requestlist(&db, 1, 3, 0, "", 1)), (vec![3, 7, 11], 1));
        // the name search is a case-sensitive substring; the empty text matches everything (14: three pages)
        assert_eq!(ids(&requestlist(&db, 1, 4, 0, "Kni", 1)), (vec![1, 5, 9], 1));
        assert_eq!(ids(&requestlist(&db, 1, 4, 0, "kni", 1)), (vec![], 1));
        assert_eq!(ids(&requestlist(&db, 1, 4, 0, "", 1)), (vec![1, 2, 3, 4, 5], 3));
        assert_eq!(ids(&requestlist(&db, 1, 4, 0, "", 2)), (vec![6, 7, 8, 9, 10], 3));
        assert_eq!(ids(&requestlist(&db, 1, 4, 0, "", 3)), (vec![11, 12, 13, 14], 3));
        assert_eq!(ids(&requestlist(&db, 1, 4, 0, "", 4)), (vec![], 3));
        // page 0 is page 1; a page of 0x8000 or more is negative and matches none
        assert_eq!(ids(&requestlist(&db, 1, 4, 0, "", 0)), (vec![1, 2, 3, 4, 5], 3));
        assert_eq!(ids(&requestlist(&db, 1, 4, 0, "", 65535)), (vec![], 3));
        // type 5 is a maximum price
        assert_eq!(ids(&requestlist(&db, 1, 5, 30, "", 1)), (vec![1, 2, 3, 13, 14], 1));
        // 6: my auctions, 7: my bids
        assert_eq!(ids(&requestlist(&db, 101, 6, 0, "", 2)), (vec![11], 2));
        assert_eq!(ids(&requestlist(&db, 7, 7, 0, "", 1)), (vec![3], 1));
        assert_eq!(ids(&requestlist(&db, 8, 7, 0, "", 1)), (vec![], 1));
        // any other type: no filter
        assert_eq!(requestlist(&db, 1, 8, 0, "zzz", 3).0.len(), 4);
        // exactly five results are one page, six are two
        assert_eq!(requestlist(&db[..5], 1, 4, 0, "", 1).1, 1);
        assert_eq!(requestlist(&db[..6], 1, 4, 0, "", 1).1, 2);
    }

    /// auction_create's INSERT: a negative price does not fit the `int unsigned` column (the oracle's char log:
    /// "DB error - Out of range value for column 'price' at row 1", int_auction.cpp:115).
    #[test]
    fn insert_refuses_values_outside_the_columns() {
        let mut a = AuctionData { price: 1000, buynow: 5000, hours: 1, ..Default::default() };
        assert!(insert_fits(&a));
        a.price = -1;
        assert!(!insert_fits(&a));
        a.price = 0;
        assert!(insert_fits(&a));
        a.item.refine = 200; // (char)200 is -56
        assert!(!insert_fits(&a));
    }

    /// inter_auctions_fromsql keeps of an item what the `auction` table stores.
    #[test]
    fn sql_item_keeps_the_table_columns() {
        let mut it = Item::new(1201);
        it.amount = 1;
        it.identify = 1;
        it.refine = 7;
        it.attribute = 1;
        it.card0 = 4001;
        it.card3 = 4002;
        it.bound = 1;
        it.equip = 2;
        it.favorite = 1;
        it.unique_id = 77;
        it.enchantgrade = 2;
        it.expire_time = 99;
        let o = sql_item(&it);
        assert_eq!((o.nameid, o.refine, o.attribute, o.card0, o.card3, o.unique_id, o.enchantgrade), (1201, 7, 1, 4001, 4002, 77, 2));
        assert_eq!((o.amount, o.identify, o.expire_time, o.bound, o.equip, o.favorite), (1, 1, 0, 0, 0, 0));
    }
}
