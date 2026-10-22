//! @autotrade and the autotrade persistence: atcommand.cpp ACMD_FUNC(autotrade), sd->state.autotrade /
//! sd->autotrade_tid / sd->state.keepshop, clif_parse's end-of-file branch that keeps an autotrader in the world
//! without its connection, pc.cpp pc_autotrade_timer, and the restore of autotraders when the server starts:
//! vending.cpp do_init_vending_autotrade / vending_reopen / vending_autotrader_remove / _free and buyingstore.cpp
//! do_init_buyingstore_autotrade / buyingstore_reopen / buyingstore_autotrader_remove / _free / buyingstore_update,
//! with the buying store's SQL mirror (`buyingstores` / `buyingstore_items`; the vending mirror is vending.rs').
//!
//! A character without a client (rAthena's `sd->fd == 0`) is a `pc` row with `disconnected` set whose `identity` is
//! [`no_fd_identity`]: no client ever has that identity, so nothing is sent to it, and the client that used
//! @autotrade can log in again with any account (clif_parse: `session[fd]->session_data = nullptr; sd->fd = 0`).
//!
//! When the server starts: rAthena's restart is, for the module, the world build (npc.rs build_world, "the rebuild
//! stands for a server start"). Before the old world goes, the map server's shutdown kicks every player with
//! state.keepshop set (map.cpp MapServer::handle_shutdown → clif_GM_kick(nullptr, sd)): an autotrader (no fd) leaves
//! through map_quit at once and its shop rows stay ([`world_shutdown`]). Once the new world is built, the char
//! server connection is ready (chrif.cpp chrif_on_ready) and the autotraders are loaded from the rows and log in
//! again ([`chrif_on_ready`]). Deliberately different: players with a client stay in the world over the module's
//! restart (a SpacetimeDB publish disconnects them itself), so the rows of shops still open are kept where rAthena
//! empties both tables.

use spacetimedb::{table, Identity, SpacetimeType, Table};

use crate::clif::{self, Target};
use crate::pc::{self, pc as _};
use crate::unit;
use crate::world::Ctx;

/// conf/battle/feature.conf `feature.autotrade: on`, `feature.autotrade_direction: 4`,
/// `feature.autotrade_head_direction: 0`, `feature.autotrade_sit: 1`, `feature.autotrade_open_delay: 5000`;
/// conf/battle/misc.conf `at_mapflag: no` (autotrade_mapflag), `at_timeout: 0`, `at_monsterignore: no`
/// (autotrade_monsterignore), `at_logout_event: yes`.
pub fn feature_autotrade() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_AUTOTRADE) != 0
}
pub fn feature_autotrade_direction() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_AUTOTRADE_DIRECTION)
}
pub fn feature_autotrade_head_direction() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_AUTOTRADE_HEAD_DIRECTION)
}
pub fn feature_autotrade_sit() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_AUTOTRADE_SIT)
}
pub fn feature_autotrade_open_delay() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_AUTOTRADE_OPEN_DELAY) as i64
}
pub fn autotrade_mapflag() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::AT_MAPFLAG)
}
pub fn at_timeout() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::AT_TIMEOUT)
}
pub fn autotrade_monsterignore() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::AT_MONSTERIGNORE) != 0
}
pub fn at_logout_event() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::AT_LOGOUT_EVENT) != 0
}
/// map.hpp e_mapflag MF_AUTOTRADE.
pub const MF_AUTOTRADE: i16 = 47;

// ---------------------------------------------------------------------------------------------- state

/// sd->state.autotrade (1 autotrading; the restore marks 2 = vending, 4 = buying store to reopen), sd->autotrade_tid
/// and sd->state.keepshop of a character in the world.
#[table(accessor = pc_autotrade)]
#[derive(Clone, Debug)]
pub struct PcAutotrade {
    #[primary_key]
    pub account_id: u32,
    pub autotrade: u8,
    pub autotrade_tid: u64,
    pub keepshop: bool,
}

fn row(c: &Ctx, aid: u32) -> PcAutotrade {
    c.db().pc_autotrade().account_id().find(aid).unwrap_or(PcAutotrade { account_id: aid, autotrade: 0, autotrade_tid: 0, keepshop: false })
}

fn put(c: &Ctx, r: PcAutotrade) {
    if c.db().pc_autotrade().account_id().find(r.account_id).is_some() {
        c.db().pc_autotrade().account_id().update(r);
    } else {
        c.db().pc_autotrade().insert(r);
    }
}

/// clif.cpp clif_GM_kick(nullptr, tsd)'s `tsd->state.keepshop = true`: the shop stays over the quit (the server's
/// or a script's kick).
pub fn set_keepshop(c: &Ctx, aid: u32) {
    if pc::get(c, aid).is_none() {
        return;
    }
    let mut r = row(c, aid);
    r.keepshop = true;
    put(c, r);
}

/// sd->state.autotrade (0 for a player without it).
pub fn autotrade(c: &Ctx, aid: u32) -> u8 {
    c.db().pc_autotrade().account_id().find(aid).map(|r| r.autotrade).unwrap_or(0)
}

fn set_autotrade(c: &Ctx, aid: u32, v: u8) {
    let mut r = row(c, aid);
    r.autotrade = v;
    put(c, r);
}

/// unit_free: the state goes with the session.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    c.db().pc_autotrade().account_id().delete(aid);
}

/// sd->fd = 0: the identity of a character without a client (never a real client's: SpacetimeDB identities are
/// hashes; this one is the bytes "LOKI-AT" and the account id).
pub fn no_fd_identity(aid: u32) -> Identity {
    let mut b = [0u8; 32];
    b[..7].copy_from_slice(b"LOKI-AT");
    b[28..].copy_from_slice(&aid.to_be_bytes());
    Identity::from_be_byte_array(b)
}

/// clif.cpp clif_parse's end-of-file branch for an autotrader: "Disassociate character from the socket
/// connection" (`session[fd]->session_data = nullptr; sd->fd = 0`). Called by session.rs clif_parse_eof in place of
/// clif_quitsave.
pub fn disassociate(c: &Ctx, p: &mut pc::Pc) {
    let name = unit::get(c, p.account_id).map(|u| u.name).unwrap_or_default();
    p.disconnected = true;
    p.identity = no_fd_identity(p.account_id);
    pc::put(c, p);
    log::info!("Character '{name}' logged off (using @autotrade).");
}

// ---------------------------------------------------------------------------------------------- @autotrade

/// chrif.cpp chrif_save(sd, CSAVE_AUTOTRADE): the character is saved as one that quits (status changes, storage,
/// guild storage when open, quest log, achievements; the storage window is force-closed), without the inventory /
/// cart item save and without logging it out (chrif.rs chrif_save for the common part). (The module's status changes
/// are table rows that live on: chrif_save_scdata's copy is what the final save at map_quit writes;
/// chrif_bsdata_save(sd, quitting && !autotrade) keeps the bonus script timers running.)
pub fn chrif_save_autotrade(c: &Ctx, aid: u32) {
    crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_AUTOTRADE);
    // if (flag&CSAVE_QUITTING) sd->state.storage_flag = 0; //Force close it. (after the guild storage save; the
    // quest and achievement saves that follow it in chrif_save do not read the flag)
    if let Some(mut p) = pc::get(c, aid) {
        if p.storage_flag != 0 {
            p.storage_flag = 0;
            pc::put(c, &p);
        }
    }
}

/// atcommand.cpp ACMD_FUNC(autotrade): the shop stays open without the client.
pub fn atcommand_autotrade(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    let (Some(sd), Some(p)) = (unit::get(c, aid), pc::get(c, aid)) else { return -1 };
    if crate::npc::mapflag_value(c, sd.map, MF_AUTOTRADE) != autotrade_mapflag() {
        say(crate::data::msg_txt(1179)); // Autotrade is not allowed on this map.
        return -1;
    }
    if pc::isdead(c, aid) {
        say(crate::data::msg_txt(1180)); // You cannot autotrade when dead.
        return -1;
    }
    if !p.vending && !p.buyingstore {
        //check if player is vending or buying
        say(crate::data::msg_txt(549)); // "You should have a shop open to use @autotrade."
        return -1;
    }
    set_autotrade(c, aid, 1);
    if autotrade_monsterignore() {
        if let Some(mut p) = pc::get(c, aid) {
            p.block_action |= pc::PCBLOCK_IMMUNE;
            pc::put(c, &p);
        }
    }
    if p.vending {
        crate::vending::vending_update(c, aid);
    } else if p.buyingstore {
        buyingstore_update(c, aid);
    }
    if at_timeout() != 0 {
        // status_change_start(nullptr, sd, SC_AUTOTRADE, 10000, 0, 0, 0, 0, min(timeout, at_timeout) * 60000, SCSTART_NONE)
        let timeout = crate::cstr::atoi(message);
        let minutes = if timeout > 0 { timeout.min(at_timeout()) } else { at_timeout() };
        crate::status::sc::status_change_start(c, None, &sd, crate::status::consts::SC_AUTOTRADE, 10000, 0, 0, 0, 0, minutes as i64 * 60000, crate::status::defs::SCSTART_NONE, 0);
    }
    if at_logout_event() {
        crate::script::npc_run::npc_script_event(c, aid, crate::script::npc_run::NPCE_LOGOUT);
    }
    crate::channel::channel_pcquit(c, aid, 0xF); //leave all chan
    // SC_NOTIFY_BAN 15, then set_eof: clif_parse's end-of-file branch keeps the autotrader in the world once this
    // command is done (session.rs clif_parse_eof → disassociate)
    crate::session::clif_authfail_fd(c, aid, 15);
    chrif_save_autotrade(c, aid);
    0
}

// ---------------------------------------------------------------------------------------------- map_quit / timers

/// map.cpp map_quit's first part: `if (sd->state.keepshop == false)` the vending shop or the buying store closes;
/// the autotrade timer is deleted with the other timers of the character.
pub fn map_quit(c: &Ctx, aid: u32) {
    let Some(p) = pc::get(c, aid) else { return };
    if !row(c, aid).keepshop {
        // Close vending/buyingstore
        if p.vending {
            crate::vending::vending_closevending(c, aid);
        } else if p.buyingstore {
            crate::buyingstore::buyingstore_close(c, aid);
        }
    }
    let r = row(c, aid);
    if r.autotrade_tid != 0 {
        crate::timer::delete(c, r.autotrade_tid);
        let mut r = r;
        r.autotrade_tid = 0;
        put(c, r);
    }
}

/// pc.cpp pc_autotrade_timer (Func::PcAutotradeTimer, target = account id): the restored autotrader's shop opens.
pub fn pc_autotrade_timer(c: &Ctx, aid: u32) {
    if pc::get(c, aid).is_none() {
        return;
    }
    let mut r = row(c, aid);
    r.autotrade_tid = 0;
    put(c, r.clone());
    if r.autotrade & 2 != 0 {
        vending_reopen(c, aid);
    }
    if autotrade(c, aid) & 4 != 0 {
        buyingstore_reopen(c, aid);
    }
    // (a failed reopen has already taken the character out: map_quit)
    if pc::get(c, aid).is_none() {
        return;
    }
    if crate::vending::vender_id(c, aid) == 0 && crate::buyingstore::bs(c, aid).buyer_id == 0 {
        set_autotrade(c, aid, 0);
        pc::quit(c, aid);
    }
}

// ---------------------------------------------------------------------------------------------- the restore

/// buyingstore.hpp struct s_autotrade_entry.
#[derive(SpacetimeType, Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct AutotradeEntry {
    /// Item entry id / cartinventory_id in cart_inventory (vending).
    pub cartinventory_id: u32,
    pub amount: u16,
    pub price: u32,
    /// Item index in cart.
    pub index: u16,
    /// Item ID (buying store).
    pub item_id: u32,
}

/// buyingstore.hpp struct s_autotrader.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq, Eq)]
pub struct Autotrader {
    /// vendor / buyer id.
    pub id: u32,
    pub account_id: u32,
    pub char_id: u32,
    pub sex: u8,
    pub dir: u8,
    pub head_dir: u8,
    pub sit: u8,
    pub title: String,
    /// Maximum zeny to be spent (buying store).
    pub limit: u32,
    pub count: u16,
    pub entries: Vec<AutotradeEntry>,
}

/// vending.cpp vending_autotrader_db: the autotraders loaded at the start, by char id, until their shop reopens.
#[table(accessor = autotrader_vending)]
#[derive(Clone, Debug)]
pub struct AutotraderVending {
    #[primary_key]
    pub char_id: u32,
    pub at: Autotrader,
}

/// buyingstore.cpp buyingstore_autotrader_db.
#[table(accessor = autotrader_buyingstore)]
#[derive(Clone, Debug)]
pub struct AutotraderBuyingstore {
    #[primary_key]
    pub char_id: u32,
    pub at: Autotrader,
}

/// map.cpp MapServer::handle_shutdown for the characters without a client: clif_GM_kick(nullptr, sd) sets
/// state.keepshop and, the session not being active, map_quit takes them out at once (their shop rows stay for the
/// restore). Called by npc.rs build_world before the old world is torn down.
pub fn world_shutdown(c: &Ctx) {
    // mapit_getallusers: the characters in the world, in pc_db's iteration order
    let mut users: Vec<u32> = c.db().pc().iter().filter(|p| p.disconnected && autotrade(c, p.account_id) != 0).map(|p| p.account_id).collect();
    users.sort_by_key(|&aid| crate::party::idb_order(aid));
    for aid in users {
        // clif_GM_kick(nullptr, sd): keepshop, and map_quit at once (no active session)
        crate::session::clif_gm_kick(c, None, aid);
    }
}

/// chrif.cpp chrif_on_ready's first-connection part: `do_init_buyingstore_autotrade(); do_init_vending_autotrade();`.
/// Called by npc.rs build_world once the new world stands.
pub fn chrif_on_ready(c: &Ctx) {
    do_init_buyingstore_autotrade(c);
    do_init_vending_autotrade(c);
}

/// buyingstore.cpp / vending.cpp: the `dir`, `head_dir`, `sit` of a row, overridden by the feature.autotrade_* settings.
fn with_feature_settings(at: &mut Autotrader) {
    if feature_autotrade_direction() >= 0 {
        at.dir = feature_autotrade_direction() as u8;
    }
    if feature_autotrade_head_direction() >= 0 {
        at.head_dir = feature_autotrade_head_direction() as u8;
    }
    if feature_autotrade_sit() >= 0 {
        at.sit = feature_autotrade_sit() as u8;
    }
}

/// safestrncpy(at->title, data, zmin(len + 1, MESSAGE_SIZE)).
fn title_of(s: &str) -> String {
    s.chars().take(crate::vending::MESSAGE_SIZE - 1).collect()
}

/// vending.cpp do_init_vending_autotrade.
pub fn do_init_vending_autotrade(c: &Ctx) {
    use crate::vending::{vending_items as _, vendings as _};
    if feature_autotrade() {
        // SELECT … FROM `vendings` WHERE `autotrade` = 1 AND (SELECT COUNT(`vending_id`) FROM `vending_items`
        // WHERE `vending_id` = `id`) > 0 ORDER BY `id`
        let mut rows: Vec<crate::vending::Vendings> =
            c.db().vendings().iter().filter(|r| r.autotrade == 1 && c.db().vending_items().by_vending().filter(r.id).next().is_some()).collect();
        rows.sort_by_key(|r| r.id);
        if !rows.is_empty() {
            let mut items = 0u16;
            // Init each autotrader data
            for r in &rows {
                let mut at = Autotrader {
                    id: r.id,
                    account_id: r.account_id,
                    char_id: r.char_id,
                    sex: if r.sex.starts_with('F') { 0 } else { 1 },
                    title: title_of(&r.title),
                    dir: r.body_direction,
                    head_dir: r.head_direction,
                    sit: r.sit,
                    count: 0,
                    ..Default::default()
                };
                with_feature_settings(&mut at);
                // pc_setnewpc(fd 0) + state.autotrade = 1|2 (+ PCBLOCK_IMMUNE by at_monsterignore) +
                // chrif_authreq(at->sd, true): the character logs in when the char server answers
                autotrader_login(c, &at, 1 | 2);
                upsert_vending(c, at);
            }
            // Init items for each autotraders
            for at_row in autotrader_vending_db(c) {
                let mut at = at_row.at;
                let list = crate::vending::sql_items_of(c, at.id);
                at.count = list.len() as u16;
                if at.count == 0 {
                    pc::quit(c, at.account_id);
                    vending_autotrader_remove(c, at.char_id);
                    continue;
                }
                //Init the list
                at.entries = list.iter().map(|e| AutotradeEntry { cartinventory_id: e.cartinventory_id, amount: e.amount, price: e.price, ..Default::default() }).collect();
                items = items.wrapping_add(at.entries.len() as u16);
                upsert_vending(c, at);
            }
            log::info!("Done loading '{}' vending autotraders with '{items}' items.", c.db().autotrader_vending().count());
        }
    }
    // Everything is loaded fine, their entries will be reinserted once they are loaded
    // (DELETE FROM `vendings` / `vending_items`: the rows of shops still open in the module stay, see the module doc)
    let open: Vec<u32> = crate::vending::vending_db(c).iter().map(|&aid| crate::vending::vender_id(c, aid)).collect();
    let ids: Vec<u32> = c.db().vendings().iter().map(|r| r.id).filter(|id| !open.contains(id)).collect();
    for id in ids {
        c.db().vendings().id().delete(id);
    }
    let rows: Vec<u64> = c.db().vending_items().iter().filter(|r| !open.contains(&r.vending_id)).map(|r| r.row_id).collect();
    for r in rows {
        c.db().vending_items().row_id().delete(r);
    }
}

/// The autotrader db in rAthena's uidb iteration order (keyed by char id).
fn autotrader_vending_db(c: &Ctx) -> Vec<AutotraderVending> {
    let mut v: Vec<AutotraderVending> = c.db().autotrader_vending().iter().collect();
    v.sort_by_key(|r| crate::party::idb_order(r.char_id));
    v
}

fn autotrader_buyingstore_db(c: &Ctx) -> Vec<AutotraderBuyingstore> {
    let mut v: Vec<AutotraderBuyingstore> = c.db().autotrader_buyingstore().iter().collect();
    v.sort_by_key(|r| crate::party::idb_order(r.char_id));
    v
}

fn upsert_vending(c: &Ctx, at: Autotrader) {
    let r = AutotraderVending { char_id: at.char_id, at };
    if c.db().autotrader_vending().char_id().find(r.char_id).is_some() {
        c.db().autotrader_vending().char_id().update(r);
    } else {
        c.db().autotrader_vending().insert(r);
    }
}

fn upsert_buyingstore(c: &Ctx, at: Autotrader) {
    let r = AutotraderBuyingstore { char_id: at.char_id, at };
    if c.db().autotrader_buyingstore().char_id().find(r.char_id).is_some() {
        c.db().autotrader_buyingstore().char_id().update(r);
    } else {
        c.db().autotrader_buyingstore().insert(r);
    }
}

/// vending.cpp vending_autotrader_remove(at, true).
fn vending_autotrader_remove(c: &Ctx, char_id: u32) {
    c.db().autotrader_vending().char_id().delete(char_id);
}

/// buyingstore.cpp buyingstore_autotrader_remove(at, true).
fn buyingstore_autotrader_remove(c: &Ctx, char_id: u32) {
    c.db().autotrader_buyingstore().char_id().delete(char_id);
}

/// pc_setnewpc(at->sd, account_id, char_id, 0, gettick(), sex, fd 0) with sd->state.autotrade = `bits`, then
/// chrif_authreq(at->sd, true). The char server's answer (char_mapif.cpp chmapif_parse_reqauth's autotrade branch:
/// group 0, no expiration) arrives on the inter link after the caller has finished, and runs chrif_authok → pc_authok
/// ([`chrif_authok_autotrade`]).
fn autotrader_login(c: &Ctx, at: &Autotrader, bits: u8) {
    // chrif_authreq: `if( node != nullptr || !chrif_isconnected() ) { set_eof(sd->fd); return; }` — an account whose
    // character is already in the world (or being loaded) is not loaded twice
    if pc::get(c, at.account_id).is_some() {
        return;
    }
    crate::intif::send(crate::intif::Inter::Vending(crate::vending::VendingInter::AutotradeAuth { account_id: at.account_id, char_id: at.char_id, autotrade: bits }));
}

/// chrif.cpp chrif_authok for an autotrader → pc.cpp pc_authok (with the char server's group 0), pc_reg_received's
/// loads, and intif_parse_StorageReceived(TABLE_CART)'s autotrade branch: clif_parse_LoadEndAck is invoked for the
/// character, and pc_autotrade_timer is armed feature.autotrade_open_delay later. (rAthena reaches TABLE_CART before
/// the inventory and the status changes arrive; the module loads a character in one step, so LoadEndAck runs after
/// the whole load. Nobody is in view of an autotrader at a rAthena start.)
pub fn chrif_authok_autotrade(c: &Ctx, account_id: u32, char_id: u32, bits: u8) {
    // if ( ( sd = map_id2sd(account_id) ) != nullptr ) return;
    if pc::get(c, account_id).is_some() {
        return;
    }
    // sd->state.autotrade is set on the new session before the auth request
    put(c, PcAutotrade { account_id, autotrade: bits, autotrade_tid: 0, keepshop: false });
    if autotrade_monsterignore() {
        // at->sd->state.block_action |= PCBLOCK_IMMUNE (set on the `pc` row once it exists, below)
    }
    if !pc::authok_autotrade(c, no_fd_identity(account_id), account_id, char_id) {
        c.db().pc_autotrade().account_id().delete(account_id);
        return;
    }
    if autotrade_monsterignore() {
        if let Some(mut p) = pc::get(c, account_id) {
            p.block_action |= pc::PCBLOCK_IMMUNE;
            pc::put(c, &p);
        }
    }
    // intif_parse_StorageReceived TABLE_CART: `if (sd->state.autotrade) { clif_parse_LoadEndAck(sd->fd, sd);
    // sd->autotrade_tid = add_timer(gettick() + feature_autotrade_open_delay, pc_autotrade_timer, sd->id, 0); }`
    pc::load_end_ack(c, account_id);
    if pc::get(c, account_id).is_none() {
        return;
    }
    let tid = crate::timer::add(c, c.tick() + feature_autotrade_open_delay(), crate::timer::Func::PcAutotradeTimer, account_id, 0);
    let mut r = row(c, account_id);
    r.autotrade_tid = tid;
    put(c, r);
}

/// pc_setdir + clif_changed_dir(AREA_WOS), and when `sit`: pc_setsit + skill_sit(sd, 1) + clif_sitting — "Make
/// vendor look perfect" (vending_reopen / buyingstore_reopen).
fn look_perfect(c: &Ctx, aid: u32, at: &Autotrader) {
    let Some(mut sd) = unit::get(c, aid) else { return };
    // pc_setdir(sd, dir, head_dir)
    sd.ud.dir = at.dir;
    sd.head_dir = at.head_dir;
    unit::save(c, &sd);
    clif::changed_dir(c, &sd, Target::AreaWos);
    if at.sit != 0 {
        // pc_setsit
        let Some(mut sd) = unit::get(c, aid) else { return };
        unit::stop_walking(c, &mut sd, unit::USW_FIXPOS | unit::USW_MOVE_FULL_CELL, 0);
        unit::stop_attack(c, &mut sd);
        sd.vd.dead_sit = 2;
        unit::save(c, &sd);
        if let Some(mut p) = pc::get(c, aid) {
            p.dead_sit = 2;
            pc::put(c, &p);
        }
        // skill_sit(sd, 1): the sitting icon (clif_status_load EFST_SIT 1); its Gangster's Paradise / TK rest-area
        // counting (RG_GANGSTER, TK_HPTIME / TK_SPTIME) is status2's (skill/iface.rs skill_sit), as for every sit
        pc::status_icon(c, &sd, pc::EFST_SIT, true);
        clif::sitting(c, &sd);
    }
}

/// vending.cpp vending_reopen: the restored autotrader's shop opens from its rows.
pub fn vending_reopen(c: &Ctx, aid: u32) {
    let Some(p) = pc::get(c, aid) else { return };
    let mut fail: i8 = -1;
    let at = c.db().autotrader_vending().char_id().find(p.char_id).map(|r| r.at);
    // Open vending for this autotrader
    if let Some(at) = at.as_ref().filter(|a| a.count != 0 && !a.entries.is_empty()) {
        // Init vending data for autotrader
        let mut data: Vec<crate::vending::OpenEntry> = Vec::with_capacity(at.count as usize);
        for entry in at.entries.iter().take(at.count as usize) {
            // Find item position in cart (an empty slot has id 0)
            let Some(idx) = (0..crate::inventory::MAX_CART).find(|&i| crate::vending::cart_mem_id(c, aid, i) as u32 == entry.cartinventory_id) else {
                continue; // count--
            };
            let nameid = crate::cart::cart_get(c, aid, idx).map(|r| r.item.nameid).unwrap_or(0);
            data.push(crate::vending::OpenEntry {
                index: idx as u16 + 2,
                amount: if crate::db::items::itemdb_isstackable(nameid) { entry.amount } else { 1 },
                value: entry.price,
            });
        }
        if let Some(mut p) = pc::get(c, aid) {
            p.prevend = true; // Set him into a hacked prevend state
            pc::put(c, &p);
        }
        set_autotrade(c, aid, 1);
        // Make sure abort all NPCs
        crate::script::npc_run::npc_event_dequeue(c, aid, true);
        crate::script::npc_run::pc_cleareventtimer(c, aid);
        // Open the vending again
        fail = crate::vending::vending_openvending(c, aid, &at.title, &data, Some(at));
        if fail == 0 {
            // Make vendor look perfect
            look_perfect(c, aid, at);
            // Immediate save
            chrif_save_autotrade(c, aid);
            let (name, map, x, y) = unit::get(c, aid).map(|u| (u.name, crate::data::maps().get(u.map).map(|d| d.name.clone()).unwrap_or_default(), u.x, u.y)).unwrap_or_default();
            log::info!("Vending loaded for '{name}' with '{}' items at {map} ({x},{y})", data.len());
        }
    }
    if let Some(at) = at {
        vending_autotrader_remove(c, at.char_id);
        // (db_size == 0: the db is cleared)
    }
    if fail != 0 {
        let name = unit::get(c, aid).map(|u| u.name).unwrap_or_default();
        log::error!("vending_reopen: (Error:{fail}) Load failed for autotrader '{name}' (CID={}/AID={aid})", p.char_id);
        pc::quit(c, aid);
    }
}

// ---------------------------------------------------------------------------------------------- buying stores

/// rAthena's SQL table `buyingstores` (sql-files/main.sql).
#[table(accessor = buyingstores)]
#[derive(Clone, Debug)]
pub struct Buyingstores {
    #[primary_key]
    pub id: u32,
    pub account_id: u32,
    pub char_id: u32,
    /// enum('F','M').
    pub sex: String,
    pub map: String,
    pub x: u16,
    pub y: u16,
    pub title: String,
    pub limit: u32,
    pub body_direction: u8,
    pub head_direction: u8,
    pub sit: u8,
    pub autotrade: i8,
}

/// rAthena's SQL table `buyingstore_items` (primary key buyingstore_id + index; the row id is only a handle).
#[table(accessor = buyingstore_items, index(accessor = by_store, btree(columns = [buyingstore_id])))]
#[derive(Clone, Debug)]
pub struct BuyingstoreItemRow {
    #[primary_key]
    #[auto_inc]
    pub row_id: u64,
    pub buyingstore_id: u32,
    pub index: u16,
    pub item_id: u32,
    pub amount: u16,
    pub price: u32,
}

/// buyingstore_create's INSERTs into `buyingstores` / `buyingstore_items` (after the store opened).
pub fn buyingstore_sql_insert(c: &Ctx, aid: u32, at: Option<&Autotrader>) {
    let (Some(sd), Some(p)) = (unit::get(c, aid), pc::get(c, aid)) else { return };
    let b = crate::buyingstore::bs(c, aid);
    let sex = pc::character_of(c, aid).map(|ch| ch.sex).unwrap_or(1);
    c.db().buyingstores().insert(Buyingstores {
        id: b.buyer_id,
        account_id: aid,
        char_id: p.char_id,
        sex: crate::vending::sex_char(sex),
        map: crate::data::maps().get(sd.map).map(|d| d.name.clone()).unwrap_or_default(),
        x: sd.x as u16,
        y: sd.y as u16,
        title: b.message.clone(),
        limit: b.zenylimit as u32,
        body_direction: at.map(|a| a.dir).unwrap_or(sd.ud.dir),
        head_direction: at.map(|a| a.head_dir).unwrap_or(sd.head_dir),
        sit: at.map(|a| a.sit).unwrap_or((sd.vd.dead_sit == 2) as u8),
        autotrade: autotrade(c, aid) as i8,
    });
    for (i, e) in b.items.iter().take(b.slots as usize).enumerate() {
        c.db().buyingstore_items().insert(BuyingstoreItemRow { row_id: 0, buyingstore_id: b.buyer_id, index: i as u16, item_id: e.nameid, amount: e.amount, price: e.price as u32 });
    }
}

/// buyingstore_close's `DELETE FROM buyingstore_items WHERE buyingstore_id = …` / `DELETE FROM buyingstores WHERE id = …`.
pub fn buyingstore_sql_delete(c: &Ctx, buyer_id: u32) {
    let rows: Vec<u64> = c.db().buyingstore_items().by_store().filter(buyer_id).map(|r| r.row_id).collect();
    for r in rows {
        c.db().buyingstore_items().row_id().delete(r);
    }
    c.db().buyingstores().id().delete(buyer_id);
}

/// buyingstore_trade's `UPDATE buyingstore_items SET amount … WHERE buyingstore_id … AND index …` (amount left) or
/// `DELETE … WHERE buyingstore_id … AND index …` (none left).
pub fn buyingstore_sql_item(c: &Ctx, buyer_id: u32, listidx: usize, amount: u16) {
    let rows: Vec<BuyingstoreItemRow> = c.db().buyingstore_items().by_store().filter(buyer_id).filter(|r| r.index as usize == listidx).collect();
    for mut r in rows {
        if amount > 0 {
            r.amount = amount;
            c.db().buyingstore_items().row_id().update(r);
        } else {
            c.db().buyingstore_items().row_id().delete(r.row_id);
        }
    }
}

/// buyingstore_trade's `UPDATE buyingstores SET limit = … WHERE id = …` (continue buying).
pub fn buyingstore_sql_limit(c: &Ctx, buyer_id: u32, limit: i32) {
    if let Some(mut r) = c.db().buyingstores().id().find(buyer_id) {
        r.limit = limit as u32;
        c.db().buyingstores().id().update(r);
    }
}

/// buyingstore.cpp buyingstore_update: the store's position, direction, sitting and autotrade flag.
pub fn buyingstore_update(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let id = crate::buyingstore::bs(c, aid).buyer_id;
    let Some(mut r) = c.db().buyingstores().id().find(id) else { return };
    r.map = crate::data::maps().get(sd.map).map(|d| d.name.clone()).unwrap_or_default();
    r.x = sd.x as u16;
    r.y = sd.y as u16;
    r.body_direction = sd.ud.dir;
    r.head_direction = sd.head_dir;
    r.sit = (sd.vd.dead_sit == 2) as u8;
    r.autotrade = autotrade(c, aid) as i8;
    c.db().buyingstores().id().update(r);
}

/// buyingstore.cpp do_init_buyingstore_autotrade.
pub fn do_init_buyingstore_autotrade(c: &Ctx) {
    if feature_autotrade() {
        // SELECT … FROM `buyingstores` WHERE `autotrade` = 1 AND `limit` > 0 AND (SELECT COUNT(`buyingstore_id`)
        // FROM `buyingstore_items` WHERE `buyingstore_id` = `id`) > 0 ORDER BY `id`
        let mut rows: Vec<Buyingstores> =
            c.db().buyingstores().iter().filter(|r| r.autotrade == 1 && r.limit > 0 && c.db().buyingstore_items().by_store().filter(r.id).next().is_some()).collect();
        rows.sort_by_key(|r| r.id);
        if !rows.is_empty() {
            let mut items = 0u16;
            // Init each autotrader data
            for r in &rows {
                let mut at = Autotrader {
                    id: r.id,
                    account_id: r.account_id,
                    char_id: r.char_id,
                    sex: if r.sex.starts_with('F') { 0 } else { 1 },
                    title: title_of(&r.title),
                    limit: r.limit,
                    dir: r.body_direction,
                    head_dir: r.head_direction,
                    sit: r.sit,
                    count: 0,
                    ..Default::default()
                };
                with_feature_settings(&mut at);
                autotrader_login(c, &at, 1 | 4);
                upsert_buyingstore(c, at);
            }
            // Init items for each autotraders
            for at_row in autotrader_buyingstore_db(c) {
                let mut at = at_row.at;
                let mut list: Vec<BuyingstoreItemRow> = c.db().buyingstore_items().by_store().filter(at.id).collect();
                list.sort_by_key(|r| (r.index, r.row_id)); // ORDER BY `index` ASC
                at.count = list.len() as u16;
                if at.count == 0 {
                    pc::quit(c, at.account_id);
                    buyingstore_autotrader_remove(c, at.char_id);
                    continue;
                }
                //Init the list
                at.entries = list.iter().map(|e| AutotradeEntry { item_id: e.item_id, amount: e.amount, price: e.price, ..Default::default() }).collect();
                items = items.wrapping_add(at.entries.len() as u16);
                upsert_buyingstore(c, at);
            }
            log::info!("Done loading '{}' buyingstore autotraders with '{items}' items.", c.db().autotrader_buyingstore().count());
        }
    }
    // Everything is loaded fine, their entries will be reinserted once they are loaded (the stores still open in the
    // module keep theirs, see the module doc)
    let open: Vec<u32> = crate::buyingstore::buyingstore_db(c).iter().map(|&aid| crate::buyingstore::bs(c, aid).buyer_id).collect();
    let ids: Vec<u32> = c.db().buyingstores().iter().map(|r| r.id).filter(|id| !open.contains(id)).collect();
    for id in ids {
        c.db().buyingstores().id().delete(id);
    }
    let rows: Vec<u64> = c.db().buyingstore_items().iter().filter(|r| !open.contains(&r.buyingstore_id)).map(|r| r.row_id).collect();
    for r in rows {
        c.db().buyingstore_items().row_id().delete(r);
    }
}

/// buyingstore.cpp buyingstore_reopen: the restored autotrader's buying store opens from its rows.
pub fn buyingstore_reopen(c: &Ctx, aid: u32) {
    let Some(p) = pc::get(c, aid) else { return };
    let mut fail: i8 = -1;
    let at = c.db().autotrader_buyingstore().char_id().find(p.char_id).map(|r| r.at);
    // Ready to open buyingstore for this char
    if let Some(at) = at.as_ref().filter(|a| a.count != 0 && !a.entries.is_empty()) {
        // Init buyingstore data for autotrader
        let data: Vec<crate::net::packets::PACKET_CZ_REQ_OPEN_BUYING_STORE_sub> = at
            .entries
            .iter()
            .take(at.count as usize)
            .map(|e| crate::net::packets::PACKET_CZ_REQ_OPEN_BUYING_STORE_sub { itemId: e.item_id, amount: e.amount, price: e.price })
            .collect();
        set_autotrade(c, aid, 1);
        // Make sure abort all NPCs
        crate::script::npc_run::npc_event_dequeue(c, aid, true);
        crate::script::npc_run::pc_cleareventtimer(c, aid);
        // Open the buyingstore again
        fail = crate::buyingstore::buyingstore_setup(c, aid, at.count as u8);
        if fail == 0 {
            fail = crate::buyingstore::buyingstore_create_at(c, aid, at.limit as i32, 1, &at.title, &data, Some(at));
        }
        if fail == 0 {
            // Make buyer look perfect
            look_perfect(c, aid, at);
            // Immediate save
            chrif_save_autotrade(c, aid);
            let (name, map, x, y) = unit::get(c, aid).map(|u| (u.name, crate::data::maps().get(u.map).map(|d| d.name.clone()).unwrap_or_default(), u.x, u.y)).unwrap_or_default();
            log::info!("Buyingstore loaded for '{name}' with '{}' items at {map} ({x},{y})", at.count);
        }
    }
    if let Some(at) = at {
        buyingstore_autotrader_remove(c, at.char_id);
    }
    if fail != 0 {
        let name = unit::get(c, aid).map(|u| u.name).unwrap_or_default();
        log::error!("buyingstore_reopen: (Error:{fail}) Load failed for autotrader '{name}' (CID={}/AID={aid})", p.char_id);
        pc::quit(c, aid);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn no_fd_identities_are_distinct() {
        assert_ne!(no_fd_identity(2000000), no_fd_identity(2000001));
        assert_eq!(no_fd_identity(7), no_fd_identity(7));
    }

    #[test]
    fn feature_settings_override_the_rows() {
        let mut at = Autotrader { dir: 2, head_dir: 1, sit: 0, ..Default::default() };
        with_feature_settings(&mut at);
        assert_eq!((at.dir, at.head_dir, at.sit), (4, 0, 1));
    }
}
