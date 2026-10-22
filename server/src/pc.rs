//! map/pc.cpp + the session parts of clif.cpp: a character in the world. `pc` holds rAthena's map_session_data
//! runtime state; the character's saved status stays in `character` (character.rs) and is written as it changes;
//! position, look and walking are the character's `unit` row (id = account id).

use spacetimedb::{table, Identity, Table};

use crate::account::{account, session, Session};
use crate::character::{character, Character};
use crate::clif::{self, Target};
use crate::map::{self, CellChk, BL_PC};
use crate::net::wire::Reader;
use crate::timer::{self, Func};
use crate::unit::{self, unit as _, ScView, Unit, UnitData, ViewData, CLR_DEAD, CLR_OUTSIGHT, CLR_RESPAWN};
use crate::world::{Ctx, Tick};

/// map_session_data runtime state for an online character.
#[table(accessor = pc)]
#[derive(Clone, Debug)]
pub struct Pc {
    #[primary_key]
    pub account_id: u32,
    #[unique]
    pub char_id: u32,
    #[unique]
    pub identity: Identity,
    /// conf/groups.yml group of the account (99 = admin).
    pub group_id: u8,
    pub client_tick: u32,
    // sd->state
    pub active: bool,
    pub pc_loaded: bool,
    pub connect_new: bool,
    pub changemap: bool,
    pub warping: bool,
    pub rewarp: bool,
    pub debug_remove_map: bool,
    /// sd->mapindex: the map the character is on or going to (none until the first pc_setpos).
    pub mapindex: Option<u16>,
    /// sd->count_rewarp: consecutive warps that landed on another warp (npc_touch_areanpc's loop breaker).
    pub count_rewarp: u8,
    /// state.dead_sit: 1 dead, 2 sitting (pc_isdead reads this; pc_issit reads the unit's vd.dead_sit).
    pub dead_sit: u8,
    /// The client has gone (fd == 0) and the character waits for clif_delayquit.
    pub disconnected: bool,
    pub pmap: u16,
    // what pc_cant_act checks
    pub npc_id: u32,
    pub chat_id: u32,
    pub npc_shopid: u32,
    pub trading: bool,
    pub vending: bool,
    pub buyingstore: bool,
    pub prevend: bool,
    pub storage_flag: u8,
    pub mail_writing: bool,
    /// Windows open that block acting (refine / stylist / expansion / barter / laphine / roulette / enchant / reform):
    /// one `UI_*` bit per sd->state flag that pc.hpp pc_cant_act2 tests; pc_setpos clears them all (pc.cpp:6953-6965).
    pub ui_open: u32,
    /// PCBLOCK_* (setpcblock script command).
    pub block_action: u32,
    // ticks
    pub canlog_tick: Tick,
    pub cantalk_tick: Tick,
    /// sd->idletime: socket.cpp last_tick (time(nullptr), seconds) at the last action idletime_option counts.
    pub idletime: Tick,
    /// time() of the last emotion (seconds).
    pub emotionlasttime: i64,
    pub invincible_timer: u64,
    /// NPC OnTouch areas the character stands in (sd->areanpc).
    pub areanpc: Vec<u32>,
    /// NPCs cloaked / uncloaked for this character (cloakonnpcself).
    pub cloaked_npc: Vec<u32>,
    pub hpmeter_visible: bool,
}

/// `Pc::ui_open` bits: sd->state.barter_open and sd->state.barter_extended_open (barter.rs). Each window owner has its
/// own bits (itemui/mod.rs 0-6 under UI_ITEMUI_MASK, these 7 and 8, roulette.rs 9, clif_client.rs 10 for the
/// inventory expansion): 0x1 / 0x2 were itemui's UI_REFINE / UI_STYLIST too, so every itemui state write cleared an
/// open barter window's flag and an open refine window read as an open barter window.
pub const UI_BARTER: u32 = 1 << 7;
pub const UI_BARTER_EXTENDED: u32 = 1 << 8;

#[cfg(test)]
mod ui_open_bits {
    /// Every `Pc::ui_open` owner writes only its own bits, so the bits must not overlap.
    #[test]
    fn window_bits_are_disjoint() {
        let owners = [crate::itemui::UI_ITEMUI_MASK, super::UI_BARTER, super::UI_BARTER_EXTENDED, crate::roulette::UI_ROULETTE, crate::clif_client::UI_INVENTORY_EXPANSION];
        for (i, a) in owners.iter().enumerate() {
            for b in &owners[i + 1..] {
                assert_eq!(a & b, 0, "ui_open bits {a:#x} and {b:#x} overlap");
            }
        }
    }
}

/// PCBLOCK_* (pc.hpp e_pcblock_action_flag).
pub const PCBLOCK_MOVE: u32 = 0x001;
pub const PCBLOCK_ATTACK: u32 = 0x002;
pub const PCBLOCK_SKILL: u32 = 0x004;
pub const PCBLOCK_USEITEM: u32 = 0x008;
pub const PCBLOCK_CHAT: u32 = 0x010;
pub const PCBLOCK_IMMUNE: u32 = 0x020;
pub const PCBLOCK_SITSTAND: u32 = 0x040;
pub const PCBLOCK_COMMANDS: u32 = 0x080;
pub const PCBLOCK_NPCCLICK: u32 = 0x100;
pub const PCBLOCK_EMOTION: u32 = 0x200;
pub const PCBLOCK_EQUIP: u32 = 0x400;
/// script.hpp:2233 PCBLOCK_ALL.
pub const PCBLOCK_ALL: u32 = 0x7FF;

/// socket.cpp last_tick: time(nullptr) of the current server loop, in seconds (what sd->idletime holds).
pub fn last_tick(c: &Ctx) -> Tick {
    c.tick() / 1000
}

pub fn get(c: &Ctx, aid: u32) -> Option<Pc> {
    // script.cpp's dummy_sd, while a script without a player runs an @command as it: a character no table holds
    // (script/dummy_sd.rs). No account has id 0 otherwise.
    if aid == crate::script::dummy_sd::ID {
        if let Some(p) = crate::script::dummy_sd::pc() {
            return Some(p);
        }
    }
    c.db().pc().account_id().find(aid)
}

pub fn put(c: &Ctx, p: &Pc) {
    if p.account_id == crate::script::dummy_sd::ID && crate::script::dummy_sd::put_pc(p) {
        return; // script.cpp's dummy_sd has no row (script/dummy_sd.rs)
    }
    c.db().pc().account_id().update(p.clone());
}

pub fn by_identity(c: &Ctx, id: Identity) -> Option<Pc> {
    c.db().pc().identity().find(id)
}

pub fn identity_of(c: &Ctx, aid: u32) -> Option<Identity> {
    get(c, aid).filter(|p| !p.disconnected).map(|p| p.identity)
}

/// clif_session_isValid: a player whose client is still there.
pub fn is_active_session(c: &Ctx, aid: u32) -> bool {
    get(c, aid).map(|p| !p.disconnected).unwrap_or(false)
}

pub fn group_level(c: &Ctx, aid: u32) -> i32 {
    get(c, aid).map(|p| crate::rules::group_level(p.group_id)).unwrap_or(0)
}

pub fn character_of(c: &Ctx, aid: u32) -> Option<Character> {
    let p = get(c, aid)?;
    c.db().character().id().find(p.char_id)
}

/// pc.hpp pc_cant_act.
pub fn cant_act(p: &Pc, opt1: i16) -> bool {
    const OPT1_BURNING: i16 = 7;
    p.npc_id != 0
        || p.chat_id != 0
        || p.vending
        || p.buyingstore
        || (opt1 != 0 && opt1 != OPT1_BURNING)
        || p.trading
        || p.storage_flag != 0
        || p.prevend
        || p.ui_open != 0
        || p.npc_shopid != 0
}

/// The player parts of unit.cpp unit_can_move: sitting, vending, buying store, PCBLOCK_MOVE, writing mail.
pub fn cant_move(c: &Ctx, u: &Unit) -> bool {
    let Some(p) = get(c, u.id) else { return true };
    u.vd.dead_sit == 2 || p.vending || p.buyingstore || p.block_action & PCBLOCK_MOVE != 0 || p.mail_writing
}

/// sd->status.body_direction follows unit_setdir.
pub fn set_body_direction(c: &Ctx, aid: u32, dir: u8) {
    if let Some(ch) = character_of(c, aid) {
        if ch.body_direction != dir {
            c.db().character().id().update(Character { body_direction: dir, ..ch });
        }
    }
}

pub fn clear_areanpc(c: &Ctx, aid: u32) {
    // unit.cpp calls npc_touchnext_areanpc(sd, false) right before `sd->areanpc.clear()` (and before
    // npc_touch_area_allnpc, see npc.rs); at LoadEndAck, where rAthena does not, the list is always empty already
    // (unit_remove_map emptied it), so the call is a no-op there
    crate::script::npc_run::npc_touchnext_areanpc(c, aid, false);
    if let Some(mut p) = get(c, aid) {
        if !p.areanpc.is_empty() {
            p.areanpc.clear();
            put(c, &p);
        }
    }
}

/// Visible look of a character (status_set_viewdata + pc look fields).
pub fn view_of(ch: &Character) -> ViewData {
    use crate::db::jobs::ids::*;
    let mut class = ch.class as i32;
    // Adapt class to a Mounted one (status.cpp:9400-9422: sc.option is the saved option at pc_authok)
    if clif::pcdb_checkid(class) && ch.option & crate::status::defs::OPTION_RIDING != 0 {
        class = match class {
            JOB_KNIGHT => JOB_KNIGHT2,
            JOB_CRUSADER => JOB_CRUSADER2,
            JOB_LORD_KNIGHT => JOB_LORD_KNIGHT2,
            JOB_PALADIN => JOB_PALADIN2,
            JOB_BABY_KNIGHT => JOB_BABY_KNIGHT2,
            JOB_BABY_CRUSADER => JOB_BABY_CRUSADER2,
            other => other,
        };
    }
    ViewData {
        class,
        hair: ch.hair as i32,
        weapon: ch.weapon as i32,
        head_bottom: ch.head_bottom as i32,
        head_top: ch.head_top as i32,
        head_mid: ch.head_mid as i32,
        hair_color: ch.hair_color as i32,
        clothes_color: ch.clothes_color as i32,
        shield: ch.shield as i32,
        shoes: 0,
        body: 0,
        robe: ch.robe as i32,
        body2: ch.body as i32,
        sex: ch.sex,
        dead_sit: 0,
    }
}

// ---------------------------------------------------------------------------------------------- entering the map

/// clif.cpp clif_parse_WantToConnection (CZ_ENTER2 0x0436 and every other packet id bound to it, each with its own
/// field offsets) with the char server's part (chrif_authreq → pc_authok): the caller's SpacetimeDB identity must be
/// logged in as this account with this character selected on the character screen — the module's equivalent of the
/// char server's auth node, used once. `rest` is what is left of the received buffer from this packet on (RFIFOREST).
/// Returns false when the connect packet was refused: rAthena then drops the rest of the buffer (RFIFOSKIP, set_eof).
pub fn want_to_connection(c: &Ctx, pkt: &[u8], rest: usize) -> bool {
    let who = c.sender();
    if let Some(me) = by_identity(c, who) {
        if !me.disconnected {
            log::error!("clif_parse_WantToConnection : invalid request (character already logged in)");
            return true;
        }
    }
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    let pos = crate::net::packets::packet_pos(cmd);
    let rd = |i: usize| Reader::at(pkt, pos.get(i).copied().unwrap_or(0) as usize);
    let account_id = rd(0).u32().unwrap_or(0);
    let char_id = rd(1).u32().unwrap_or(0);
    let login_id1 = rd(2).u32().unwrap_or(0);
    let client_tick = rd(3).u32().unwrap_or(0);
    let sex = rd(4).u8().unwrap_or(0xff);
    let _ = login_id1; // the SpacetimeDB identity replaces the login-server session ids
    // clif_parse_WantToConnection_sub: packet_len = (int16)RFIFOREST(fd) must be the packet's length
    let err = if rest as u16 as i16 != crate::net::packets::packet_len(cmd).unwrap_or(0) {
        1 // wrong length
    } else if (account_id as i32) < crate::rules::START_ACCOUNT_ID as i32 || account_id as i32 > END_ACCOUNT_NUM {
        2 // invalid account_id
    } else if char_id as i32 <= 0 {
        3 // invalid char_id
    } else if sex != 0 && sex != 1 {
        6 // invalid sex
    } else {
        0
    };
    if err != 0 {
        log::info!("clif_parse: Disconnecting session with unknown connect packet 0x{cmd:04x}(length:{rest}) (error {err})");
        refuse_connect(c, who, err);
        return false;
    }
    // Check for double login.
    if unit::get(c, account_id).map(|u| u.kind != BL_PC).unwrap_or(false) {
        log::error!("clif_parse_WantToConnection: a non-player object already has id {account_id}, please increase the starting account number");
        refuse_connect(c, who, 3); // Rejected by server
        return false;
    }
    // the identity's login binding (the module's auth node, checked by the char server in chrif_authreq)
    let sess = c.db().session().identity().find(who);
    let authed = sess.as_ref().map(|s| s.account_id == account_id && s.selected_char == Some(char_id)).unwrap_or(false);
    if by_identity(c, who).is_some() || get(c, account_id).is_some() {
        // Still recognizes last connection: this client's previous character is still waiting out clif_delayquit, or
        // the account is in the world. There rAthena's login server has already had the other client kicked
        // (mapif_disconnectplayer reason 2) when this one logged in; here the kick comes now, and only for a client
        // that did log in as this account. The next attempt gets in once that character has left (at once, or after
        // prevent_logout's delay).
        authfail(c, who, 8);
        if authed {
            disconnectplayer(c, account_id, 2);
        }
        return true;
    }
    // ZC_AID (0x0283): the answer to CZ_ENTER before authentication completes (PACKETVER >= 20070521)
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x0283u16.to_le_bytes());
    w.extend_from_slice(&account_id.to_le_bytes());
    c.send(who, &w);
    // chrif_authreq → the char server's auth node → pc_authok, or chrif_authfail → clif_authfail_fd(0)
    let (Some(sess), true) = (sess, authed) else {
        authfail(c, who, 0);
        return true;
    };
    let Some(mut ch) = c.db().character().id().find(char_id).filter(|ch| ch.account_id == account_id) else {
        authfail(c, who, 0);
        return true;
    };
    let Some(acc) = c.db().account().id().find(account_id) else {
        authfail(c, who, 0);
        return true;
    };
    // only use the auth once and mark user online (char_mapif.cpp:1075-1077: the auth node is erased, and
    // char_set_char_online writes `last_login`)
    c.db().session().identity().update(Session { selected_char: None, ..sess });
    ch.last_login = Some(c.rc.timestamp);
    // (pc_authok compares sex only after copying the char server's status over it, so it never refuses; the char
    // server's own check is compiled out for PACKETVER >= 20141016)
    // the group the account plays with (release.rs: the owner's administrator is 99 on a release server)
    let acc = crate::account::Account { group_id: crate::release::effective_group(c.db(), &acc), ..acc };
    authok(c, who, &acc, ch, client_tick);
    true
}

/// common/mmo.hpp END_ACCOUNT_NUM.
const END_ACCOUNT_NUM: i32 = 100_000_000;

/// ZC_REFUSE_ENTER-style reply for a malformed connect packet (clif.cpp sends 0x006a err and closes).
fn refuse_connect(c: &Ctx, who: Identity, err: u8) {
    c.send(who, &[0x6a, 0x00, err, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
}

/// clif.cpp clif_authfail_fd: SC_NOTIFY_BAN.
fn authfail(c: &Ctx, who: Identity, kind: u8) {
    c.send(who, &[0x81, 0x00, kind]);
}

/// pc.cpp pc_setnewpc + pc_authok + pc_reg_received: the character enters the world.
fn authok(c: &Ctx, who: Identity, acc: &crate::account::Account, mut ch: Character, client_tick: u32) {
    let tick = c.tick();
    // char_mapif.cpp's auth answer (chmapif_parse_reqauth): char_set_char_online → inter_guild_CharOnline, on the
    // char server before pc_authok runs
    crate::intif::send(crate::intif::Inter::GuildCharOnline { char_id: ch.id });
    //Set the map-server used job id. [Skotlex] (pc.cpp:2099-2105)
    let job_exists = |id: u16| crate::db::jobs::job_db().find(id).is_some();
    if crate::db::jobs::ids::pc_jobid2mapid(ch.class) == u64::MAX || !job_exists(ch.class) {
        log::error!("pc_authok: Invalid class {} for player {} ({}:{}). Class was changed to novice.", ch.class, ch.name, ch.account_id, ch.id);
        ch.class = crate::rules::JOB_NOVICE;
    }
    // Checks and fixes to the character that the char server cannot make (pc_authok).
    ch.hair = (ch.hair as i32).clamp(crate::conf::min_hair_style(), crate::conf::max_hair_style()) as u16;
    ch.hair_color = (ch.hair_color as i32).clamp(crate::conf::min_hair_color(), crate::conf::max_hair_color()) as u16;
    ch.clothes_color = (ch.clothes_color as i32).clamp(crate::conf::min_cloth_color(), crate::conf::max_cloth_color()) as u16;
    if !job_exists(ch.body) {
        ch.body = ch.class;
    }
    // hidden from @hide when the group may not use it: all groups on Loki may (groups.yml gives group 0 every command)
    c.db().character().id().update(ch.clone());
    let p = Pc {
        account_id: ch.account_id,
        char_id: ch.id,
        identity: who,
        group_id: acc.group_id,
        client_tick,
        active: false, // pc_setnewpc: "to be set to 1 after player is fully authed and loaded" (pc_reg_received)
        pc_loaded: true,
        connect_new: true,
        changemap: false,
        warping: false,
        rewarp: false,
        debug_remove_map: false,
        mapindex: None,
        count_rewarp: 0,
        dead_sit: 0,
        disconnected: false,
        pmap: 0,
        npc_id: 0,
        chat_id: 0,
        npc_shopid: 0,
        trading: false,
        vending: false,
        buyingstore: false,
        prevend: false,
        storage_flag: 0,
        mail_writing: false,
        ui_open: 0,
        block_action: 0,
        canlog_tick: if crate::conf::prevent_logout_trigger() & crate::conf::PLT_LOGIN != 0 { tick } else { 0 },
        cantalk_tick: tick,
        idletime: last_tick(c),
        emotionlasttime: 0,
        invincible_timer: 0,
        areanpc: Vec::new(),
        cloaked_npc: Vec::new(),
        hpmeter_visible: false,
    };
    c.db().pc().insert(p);
    // memset(&sd->inventory / cart / storage …), memset(&sd->equip_index, -1 …)
    crate::inventory::on_authok(c, ch.account_id, ch.id);
    // (map_session_data is zeroed: menuskill_*, workinprogress, the item windows' state)
    crate::itemui::on_authok(c, ch.account_id);
    // pc_authok: the skill fields of map_session_data (canskill_tick / canequip_tick, spirit timers, keep-using)
    crate::skill::state::pc_authok(c, ch.account_id);
    let u = Unit {
        id: ch.account_id,
        kind: BL_PC,
        map: 0,
        x: 0,
        y: 0,
        on_map: false,
        name: ch.name.clone(),
        vd: view_of(&ch),
        // unit_dataset zeroes the direction; clif_parse_LoadEndAck restores body_direction only with spawn_direction
        ud: UnitData { dir: if crate::conf::spawn_direction() { ch.body_direction } else { 0 }, sx: 8, sy: 8, ..Default::default() },
        sc: ScView { option: ch.option, ..Default::default() },
        speed: 136, // DEFAULT_WALK_SPEED (10% faster than rAthena's 150) until status_calc_pc
        mode: unit::PC_MODE,
        level: ch.base_level as i32,
        char_id: ch.id,
        head_dir: 0,
        font: ch.font as i16,
        manner: ch.manner,
        karma: ch.karma != 0,
        guild_id: ch.guild_id,
        emblem_id: 0,
        hp: ch.hp as i64,
        max_hp: ch.max_hp as i64,
        dead: ch.hp == 0,
        size: 0,
        boss: 0,
        invisible: false,
        npc_owner_char: 0,
    };
    if c.db().unit().id().find(u.id).is_some() {
        c.db().unit().id().update(u);
    } else {
        c.db().unit().insert(u);
    }
    // the status rows (pc_authok's regen ticks, pc_reg_received's die counter, regen_db)
    crate::status::pc_authok(c, ch.account_id);
    // char_mmo_char_fromsql's mercenary_owner_fromsql: sd->status.mer_id and the guild counters (mercenary.rs)
    crate::mercenary::pc_authok(c, ch.account_id, ch.id);
    // Check if the player's last point requires special handling (pc_lastpoint_special, script2's pc_misc.rs: nosave
    // maps, the jail; instances with instances) and if conditions apply to return the player to his savepoint
    if let Some((m, x, y)) = crate::pc_misc::pc_lastpoint_special(c, &ch) {
        ch.last_map = m;
        ch.last_x = x as u16;
        ch.last_y = y as u16;
        c.db().character().id().update(ch.clone());
    }
    // warp the player to their last point
    let (lm, lx, ly) = (ch.last_map.clone(), ch.last_x as i16, ch.last_y as i16);
    let ok = match crate::data::maps().id_of(&lm) {
        Some(m) => setpos(c, ch.account_id, m, lx, ly, CLR_OUTSIGHT) == SetPos::Ok,
        None => false,
    };
    if !ok {
        log::error!("Last_point_map {lm} not found");
        let prontera = crate::data::maps().id_of("prontera");
        if prontera.map(|m| setpos(c, ch.account_id, m, 273, 354, CLR_OUTSIGHT) != SetPos::Ok).unwrap_or(true) {
            authfail(c, who, 0);
            c.db().pc().account_id().delete(ch.account_id);
            c.db().unit().id().delete(ch.account_id);
            return;
        }
    }
    // pc_reg_received: registries, inventory and status are local tables, so authentication completes at once
    // pc_reg_received: sd->cashPoints / kafraPoints / bank_vault from the account registry (cashshop.rs)
    crate::cashshop::pc_reg_received(c, ch.account_id);
    // pc_reg_received: sd->roulette_point from the character registry, sd->roulette.prizeIdx = -1 (roulette.rs)
    crate::roulette::pc_reg_received(c, ch.account_id);
    if let Some(mut p) = get(c, ch.account_id) {
        p.active = true;
        put(c, &p);
    }
    // pc_reg_received: Restore IM_CHAR instance to the player (before party_member_joined; clif_instance_info is sent
    // at the client's load confirmation for this PACKETVER, clif_instance.rs)
    crate::instance::pc_reg_received(c, ch.account_id);
    // pc_reg_received: party_member_joined (the party's data points at this player again)
    if c.db().character().id().find(ch.id).map(|x| x.party_id > 0).unwrap_or(false) {
        crate::party::party_member_joined(c, ch.account_id);
    }
    let Some(sd) = unit::get(c, ch.account_id) else { return };
    // clif_inventory_expansion_info: ZC_EXTEND_BODYITEM_SIZE (INVENTORY_BASE_SIZE 100)
    let mut w = Vec::new();
    w.extend_from_slice(&0x0b18u16.to_le_bytes());
    w.extend_from_slice(&((ch.inventory_slots as i32 - 100).max(0) as u16).to_le_bytes());
    c.send(who, &w);
    clif::authok(c, &sd);
    // char.cpp char_mmo_char_fromsql's friend list, then pc_authok's clif_friendslist_send
    crate::social::friends_load(c, ch.id);
    crate::social::friendslist_send(c, &sd);
    if crate::conf::display_version() {
        // pc_show_version (pc.cpp:14987-15000) sends message 1295 with rAthena's git hash (the oracle, built outside
        // a git checkout, says "Cannot determine SVN/Git version."). Deliberately different: this server names
        // itself; the one line every parity log shows as a difference.
        clif::displaymessage(c, &sd, "Loki Online (a SpacetimeDB module; rules ported from rAthena e985006)");
    }
    for line in crate::conf::motd() {
        clif::displaymessage(c, &sd, line);
    }
    // "Fixes login-without-aura glitch"
    clif::changemap(c, &sd, sd.map, sd.x, sd.y);
    // Sanitize Novice characters created while new_char.txt had `skill 1,9` (Item 68): a Novice whose permanent NV_BASIC
    // level plus unspent skill points exceeds `job_level - 1` gets NV_BASIC clamped back to the points actually spent.
    if ch.class == 0 && ch.job_level < 10 {
        use crate::skill::char_skill as _;
        let earned = ch.job_level.saturating_sub(1) as u32;
        for r in c.db().char_skill().by_char().filter(ch.id).collect::<Vec<_>>() {
            if r.skill_id == crate::db::skills::NV_BASIC
                && r.flag == crate::status::defs::SKILL_FLAG_PERMANENT
                && (r.lv as u32) + ch.skill_point > earned
            {
                let spent = earned.saturating_sub(ch.skill_point) as u8;
                if spent == 0 {
                    c.db().char_skill().id().delete(r.id);
                } else {
                    c.db().char_skill().id().update(crate::skill::CharSkill { lv: spent, ..r });
                }
            }
        }
    }
    // pc_authok: pc_validate_skill(sd) (char_mmo_char_fromsql's skills as the char server stored them; skilltree.rs)
    crate::skilltree::pc_validate_skill(c, ch.account_id);
    log::info!("'{}' logged in. (AID/CID: '{}/{}', Group '{}').", ch.name, ch.account_id, ch.id, acc.group_id);
    // pc_reg_received: the Taekwon mission and the Star Gladiator feel maps / hate monsters (pc.cpp:2376-2404, before the
    // plagiarism registers)
    crate::pc_taekwon::pc_reg_received(c, ch.account_id);
    // pc_reg_received: the job change levels and the plagiarised / reproduced skill from the registers
    crate::skilltree::pc_reg_received(c, ch.account_id);
    // pc_reg_received: guild_member_joined, after the storage requests and before their answers (its clif_name_area
    // follows the login's ZC_NPCACK_MAPMOVE on the oracle)
    if c.db().character().id().find(ch.id).map(|x| x.guild_id > 0).unwrap_or(false) {
        crate::guild::guild_member_joined(c, ch.account_id);
    }
    // pc_reg_received asks the char server for the storage, cart and inventory (intif_storage_request), then
    // party / guild / clan data, pets and homunculi, skill cooldowns and bonus scripts (chrif_*_request), the mail
    // inbox, the quest log and the achievements (intif_request_*). The char server answers in that order on its one
    // socket; the inventory's answer requests the status changes (chrif_scdata_request), whose answer comes last.
    // pc_reg_received: clan_member_joined (the clan data is on the map server: its packets go out at once)
    crate::clan::pc_reg_received(c, ch.account_id);
    // intif_parse_StorageReceived (TABLE_STORAGE, TABLE_CART, TABLE_INVENTORY: status_calc_pc(SCO_FIRST|SCO_FORCE))
    crate::inventory::inventory_received(c, ch.account_id);
    // pc_reg_received's intif_request_petdata (pc.cpp:2457-2459) and the char server's answer, which comes before the
    // skill cooldowns and bonus scripts (pet.rs)
    crate::pet::pc_reg_received(c, ch.account_id);
    // pc_reg_received's intif_homunculus_requestload (pc.cpp:2462-2463) and its answer, next on the char server's
    // socket (homunculus.rs)
    crate::homunculus::pc_reg_received(c, ch.account_id);
    // then intif_mercenary_request / intif_elemental_request (pc.cpp:2464-2467) and their answers (mercenary.rs,
    // elemental.rs)
    crate::mercenary::pc_reg_received(c, ch.account_id);
    crate::elemental::pc_reg_received(c, ch.account_id);
    // pc_reg_received: map_addiddb, map_delnickdb (pc.cpp:2469-2470): the character's nick_db entry goes (clif_client.rs)
    crate::clif_client::map_delnickdb(c, ch.id);
    // chrif_skillcooldown_load (the answer to pc_reg_received's chrif_skillcooldown_request, before the bonus scripts')
    crate::skill::cooldown::chrif_skillcooldown_load(c, ch.account_id);
    // chrif_bsdata_received
    crate::status::chrif_bsdata_received(c, ch.account_id);
    // intif_Mail_requestinbox(char_id, 0, MAIL_INBOX_NORMAL): the mail inbox (mail.rs)
    crate::mail::pc_login(c, ch.account_id);
    // intif_parse_questlog: the quest log (and ZC_ALL_QUEST_LIST when it has quests)
    crate::quest::pc_login(c, ch.account_id);
    // intif_parse_achievements (feature.achievement): the achievement log, ZC_ACH_UPDATE and ZC_ALL_ACH_LIST
    crate::achievement::intif_parse_achievements(c, ch.account_id);
    // chrif_load_scdata → pc_scdata_received
    crate::status::chrif_load_scdata(c, ch.account_id);
    scdata_received(c, ch.account_id);
    // char.cpp char_set_char_online: `UPDATE char SET online = 1, last_login = NOW()` (clan cleanup, guild lists)
    if let Some(mut chr) = c.db().character().id().find(ch.id) {
        chr.last_login = Some(c.rc.timestamp);
        c.db().character().id().update(chr);
    }
    if let Some(mut acc2) = c.db().account().id().find(acc.id) {
        acc2.last_login = Some(c.rc.timestamp);
        c.db().account().id().update(acc2);
    }
    if let Some(s) = c.db().session().identity().find(who) {
        c.db().session().identity().update(Session { online: true, ..s });
    }
    // the end of pc_reg_received: channel_autojoin
    crate::channel::channel_autojoin(c, ch.account_id);
    // do_init_pc's autosave timer (autosave.rs: armed with a module instance's first login)
    crate::autosave::ensure(c);
    crate::timer::rearm(c);
}

/// The autotrade restore's login (vending.cpp / buyingstore.cpp do_init_*_autotrade → pc_setnewpc with fd 0 →
/// chrif_authreq(sd, true) → char_mapif.cpp chmapif_parse_reqauth's autotrade branch, which answers with group 0 →
/// chrif_authok → pc_authok): a character without a client enters the world. `who` is its placeholder identity
/// (autotrade.rs no_fd_identity); the character has no fd from the start, so nothing is sent to it. False when the
/// account or character is gone.
pub(crate) fn authok_autotrade(c: &Ctx, who: Identity, account_id: u32, char_id: u32) -> bool {
    let Some(acc) = c.db().account().id().find(account_id) else { return false };
    let Some(ch) = c.db().character().id().find(char_id).filter(|ch| ch.account_id == account_id) else { return false };
    // the char server's auth answer for an autotrader carries group_id 0 (WFIFOL(fd,20) = 0)
    let acc = crate::account::Account { group_id: 0, ..acc };
    authok(c, who, &acc, ch, c.tick() as u32);
    match get(c, account_id) {
        Some(mut p) => {
            p.disconnected = true; // sd->fd = 0
            put(c, &p);
            true
        }
        None => false,
    }
}

/// pc.cpp pc_scdata_received: the status changes arrived from the char server (chrif_load_scdata).
pub fn scdata_received(c: &Ctx, aid: u32) {
    crate::inventory::pc_inventory_rentals(c, aid); // Needed here to remove rentals that have Status Changes after chrif_load_scdata has finished
    crate::status::clif::weight_limit(c, aid);
    // the attendance window while today's reward is waiting (PC_PERM_ATTENDANCE, pc_attendance_*)
    crate::attendance::pc_scdata_received(c, aid);
    // sd->state.pc_loaded: the `pc` row starts loaded (the whole load happens inside the login reducer), and the
    // client's LoadEndAck always comes after it (connect_new stays 1)
    if crate::inventory::pc_iscarton(c, aid) {
        if let Some(mut it) = crate::inventory::st(c, aid) {
            it.cart_weight_max = 0; // Force a client refesh
            crate::inventory::put_st(c, &it);
        }
        crate::status::calc::status_calc_cart_weight(c, aid, crate::inventory::CALCWT_ITEM | crate::inventory::CALCWT_MAXBONUS | crate::inventory::CALCWT_CARTSTATE);
    }
    crate::status::scdata_received_soulenergy(c, aid);
}

#[derive(Debug, PartialEq, Eq)]
pub enum SetPos {
    Ok,
    MapIndex,
    /// SETPOS_AUTOTRADE: an autotrader with a shop open is not moved.
    Autotrade,
}

/// pc.cpp pc_setpos: moves the character to (m, x, y). On the same map server the client is told with
/// ZC_NPCACK_MAPMOVE and reloads; the unit re-enters the block list at its LoadEndAck.
pub fn setpos(c: &Ctx, aid: u32, m: u16, x: i16, y: i16, clrtype: u8) -> SetPos {
    let Some(md) = crate::data::maps().get(m) else { return SetPos::MapIndex };
    let Some(mut p) = get(c, aid) else { return SetPos::MapIndex };
    let Some(mut u0) = unit::get(c, aid) else { return SetPos::MapIndex };
    // Player with autotrade just causes clif glitch! @ FIXME (pc.cpp:6939)
    if crate::autotrade::autotrade(c, aid) != 0 && (crate::vending::vender_id(c, aid) != 0 || crate::buyingstore::bs(c, aid).buyer_id != 0) {
        return SetPos::Autotrade;
    }
    if crate::conf::revive_onwarp() && p.dead_sit == 1 {
        setstand(c, aid, true);
        restart_value(c, aid, 1);
        let Some(p2) = get(c, aid) else { return SetPos::MapIndex };
        p = p2;
    }
    p.changemap = p.mapindex != Some(m);
    p.warping = true;
    // workinprogress, mail_writing, refineui_open, stylist_open, inventory_expansion_confirmation, barter_open,
    // barter_extended_open, laphine_*, roulette_open, enchantgrade_open, item_reform, item_enchant_index (pc.cpp:6953-6965)
    p.ui_open = 0;
    p.mail_writing = false;
    // workinprogress = WIP_DISABLE_NONE; refineui_open, stylist_open, laphine_*, enchantgrade_open, item_reform, item_enchant_index
    crate::itemui::on_setpos(c, aid);
    if p.changemap {
        // Misc map-changing settings: the instances' user counts (pc.cpp:6968-6978)
        // (they write no `pc` row: the local copy stays current)
        crate::instance::pc_setpos_changemap(c, aid, u0.map, m);
        // pc.cpp:6980-6981: moving to a map that isn't a Battlegrounds takes a member out of its team as a deserter;
        // with the queue system bg_team_leave warps it to its entry point (or save point) right there, a nested
        // pc_setpos after which this one goes on (the player is off the map then: it is tagged for rewarping)
        put(c, &p);
        if crate::battleground::pc_setpos_changemap(c, aid, m) {
            let Some(p2) = get(c, aid) else { return SetPos::MapIndex };
            let Some(u2) = unit::get(c, aid) else { return SetPos::MapIndex };
            p = p2;
            u0 = u2;
        }
        p.pmap = u0.map;
        put(c, &p);
        // Cancel some map related stuff (pc.cpp:6984-7002).
        if !crate::status::sc::load(c, aid).empty() {
            if crate::status::sc::cant(c, aid, crate::status::consts::SCS_NOWARP) {
                return SetPos::MapIndex; // You may not get out!
            }
            setpos_map_warp_statuses(c, aid);
        }
        for i in 0..crate::inventory::EQI_MAX {
            let idx = crate::inventory::st(c, aid).map(|s| s.equip_index[i]).unwrap_or(-1);
            if idx >= 0 && crate::pc_equip::pc_isequip(c, aid, idx as usize) != crate::inventory::ITEM_EQUIP_ACK_OK {
                crate::pc_equip::pc_unequipitem(c, aid, idx as i32, 2);
            }
        }
        if crate::skillunit::conf::clear_unit_onwarp() & BL_PC != 0 {
            crate::skillunit::skill_clear_unitgroup(c, aid);
        }
        // loose_ap_on_map: yes (player.conf)
        if crate::conf::loose_ap_on_map() && crate::npc::mapflag_vs(c, m) {
            crate::status::hp::status_percent_damage(c, None, aid, 0, 0, 100, false);
        }
        crate::party::party_send_dot_remove(c, &u0); // minimap dot fix [Kevin]
        crate::guild::guild_send_dot_remove(c, &u0);
        crate::battleground::bg_send_dot_remove(c, aid);
        // castle regen reset (woe.rs), then make sure vending is allowed here (vending.rs), then the buying store
        crate::woe::pc_setpos_changemap(c, aid); // if (sd->regen.state.gc) sd->regen.state.gc = 0;
        crate::vending::pc_setpos_changemap(c, aid, m);
        crate::buyingstore::pc_setpos_checks(c, aid, true, m);
        crate::channel::channel_pcquit(c, aid, 4); // quit map chan
        let Some(p2) = get(c, aid) else { return SetPos::MapIndex };
        p = p2;
        // Remove Cloaked NPCs on map change
        p.cloaked_npc.clear();
    }
    put(c, &p);
    let (mut x, mut y) = (x, y);
    if x < 0 || x >= md.xs || y < 0 || y >= md.ys {
        log::error!("pc_setpos: attempt to place player {aid} on invalid coordinates ({}-{},{})", md.name, x, y);
        x = 0;
        y = 0;
    }
    if x == 0 && y == 0 {
        if md.xs <= 2 || md.ys <= 2 {
            log::error!("pc_setpos: map {} is too small ({}x{}) for random placement", md.name, md.xs, md.ys);
            return SetPos::MapIndex;
        }
        let span_x = (md.xs as u32 - 2).max(1);
        let span_y = (md.ys as u32 - 2).max(1);
        // pick a random walkable cell
        let mut tries = 0i64;
        loop {
            x = (c.rnd() % span_x) as i16 + 1;
            y = (c.rnd() % span_y) as i16 + 1;
            tries += 1;
            if tries > md.xs as i64 * md.ys as i64 * 3 {
                log::error!("pc_setpos: couldn't find valid coordinates on {}", md.name);
                return SetPos::Ok;
            }
            let blocked = map::getcellp(md, x, y, CellChk::ChkNoPass)
                || (!crate::conf::teleport_on_portal() && crate::npc::check_areanpc(c, 1, m, x, y, 1));
            if !blocked {
                break;
            }
        }
    }
    // a shop on a novending cell closes (pc.cpp:7107-7110; CELL_CHKNOBUYINGSTORE is never set, buyingstore.rs)
    crate::vending::pc_setpos_cell(c, aid, m, x, y);
    if u0.on_map {
        // unit_remove_map_pc: the player, then the pet (pet.rs) and the active homunculus (homunculus.rs)
        unit::remove_map(c, aid, clrtype);
        crate::pet::unit_remove_map_pc(c, aid, clrtype);
        // unit_remove_map_pc (unit.cpp:3864-3883): CLR_RESPAWN / CLR_TELEPORT become CLR_OUTSIGHT for the companions;
        // the active homunculus (homunculus.rs), then the mercenary and the elemental (companion/unit.rs)
        let hclr = if clrtype == CLR_RESPAWN || clrtype == crate::unit::CLR_TELEPORT { crate::unit::CLR_OUTSIGHT } else { clrtype };
        crate::homunculus::unit_remove_map_pc(c, aid, hclr);
        crate::companion::unit::unit_remove_map_pc(c, aid, clrtype);
        if let Some(sd) = unit::get(c, aid) {
            clif::changemap(c, &sd, m, x, y);
        }
    } else if p.active {
        // Tag the player for rewarping once the map is loaded.
        if let Some(mut p2) = get(c, aid) {
            p2.rewarp = true;
            put(c, &p2);
        }
    }
    let Some(mut p) = get(c, aid) else { return SetPos::MapIndex };
    p.mapindex = Some(m);
    p.block_action = 0;
    put(c, &p);
    let Some(mut u) = unit::get(c, aid) else { return SetPos::MapIndex };
    u.map = m;
    u.x = x;
    u.y = y;
    u.ud.to_x = x;
    u.ud.to_y = y;
    u.ud.blockedmove = false;
    u.ud.blockedskill = false;
    unit::save(c, &u);
    // Increased guild castle regen [Valaris] (woe.rs)
    crate::woe::pc_setpos_castle_regen(c, aid, m);
    // the pet follows its master (pc.cpp:7134-7140, pet.rs); Basilica cells (phase 4). Check if we are going to be
    // rewarped:
    crate::pet::pc_setpos(c, aid, m, x, y);
    // the active homunculus follows (pc.cpp:7142-7157, homunculus.rs)
    crate::homunculus::pc_setpos(c, aid, get(c, aid).map(|p| p.changemap).unwrap_or(false), m, x, y);
    // the mercenary and the elemental follow (pc.cpp:7159-7172, companion/unit.rs)
    crate::companion::unit::pc_setpos(c, aid, m, x, y);
    let rewarp_cell = crate::npc::check_areanpc(c, 1, m, x, y, 0);
    if let Some(mut p) = get(c, aid) {
        p.count_rewarp = if rewarp_cell { p.count_rewarp.saturating_add(1) } else { 0 };
        put(c, &p);
    }
    // the shop's SQL row follows the player (vending_update / buyingstore_update, pc.cpp:7183-7186)
    crate::vending::pc_setpos_end(c, aid);
    if get(c, aid).map(|p| p.buyingstore).unwrap_or(false) {
        crate::buyingstore::buyingstore_update(c, aid);
    }
    SetPos::Ok
}

/// pc_setpos's status loop on a map change (pc.cpp:6988-7001): in status_db order, a status with
/// SCF_REMOVEONMAPWARP ends, one with SCF_RESTARTONMAPWARP (and a skill) restarts its timer with the skill's duration
/// at val1.
fn setpos_map_warp_statuses(c: &Ctx, aid: u32) {
    use crate::status::consts::{SCF_REMOVEONMAPWARP, SCF_RESTARTONMAPWARP};
    use crate::status::sc;
    for s in crate::db::misc::status::status_db().iter() {
        if !sc::has_sce(c, aid, s.type_) {
            continue;
        }
        if s.has_flag(SCF_REMOVEONMAPWARP) {
            sc::status_change_end(c, aid, s.type_, 0);
        }
        if s.has_flag(SCF_RESTARTONMAPWARP) && s.skill_id > 0 {
            if let Some(mut sce) = sc::get_sce(c, aid, s.type_) {
                if sce.timer != 0 {
                    timer::delete(c, sce.timer);
                }
                let t = crate::db::skills::skill_get_time(s.skill_id, sce.val1 as u16) as i64;
                sce.timer = timer::add(c, c.tick() + t, Func::StatusChangeTimer, aid, s.type_ as i64);
                sc::put_sce(c, &sce);
            }
        }
    }
}

/// pc.cpp pc_setstand.
pub fn setstand(c: &Ctx, aid: u32, force: bool) -> bool {
    if unit::get(c, aid).is_none() || get(c, aid).is_none() {
        return false;
    }
    // Cannot stand yet
    {
        use crate::status::{consts::*, sc};
        if !force && (sc::has_sce(c, aid, SC_SITDOWN_FORCE) || sc::has_sce(c, aid, SC_BANANA_BOMB_SITDOWN)) {
            return false;
        }
    }
    crate::status::on_stand_before(c, aid); // SC_TENSIONRELAX ends
    let Some(mut u) = unit::get(c, aid) else { return false };
    let Some(mut p) = get(c, aid) else { return false };
    status_icon(c, &u, EFST_SIT, false);
    clif::standing(c, &u);
    // Reset sitting tick.
    crate::status::on_stand_after(c, aid);
    let was_dead = p.dead_sit == 1;
    p.dead_sit = 0;
    u.vd.dead_sit = 0;
    if was_dead {
        u.dead = false;
        u.ud.blockedmove = false;
        u.ud.blockedskill = false;
    }
    put(c, &p);
    unit::save(c, &u);
    if was_dead {
        crate::party::clif_party_dead(c, &u); // pc.cpp:13112-13114
    }
    true
}

/// pc.cpp pc_setrestartvalue (HP/SP on revival; type&1 normal resurrection, otherwise the values saved).
pub fn restart_value(c: &Ctx, aid: u32, kind: i32) {
    crate::status::restart_value(c, aid, kind);
}

/// status.hpp EFST_SIT.
pub const EFST_SIT: u16 = 622;

/// clif.cpp clif_status_load → clif_status_change → clif_status_change_sub (display_status_timers: yes):
/// 0x0983 with a 9999 ms "timer" when on, 0x0196 when off; to the area (only self while GM-invisible).
pub fn status_icon(c: &Ctx, u: &Unit, efst: u16, on: bool) {
    let mut w = Vec::with_capacity(29);
    if on {
        w.extend_from_slice(&0x0983u16.to_le_bytes());
        w.extend_from_slice(&efst.to_le_bytes());
        w.extend_from_slice(&u.id.to_le_bytes());
        w.push(1);
        w.extend_from_slice(&9999u32.to_le_bytes());
        w.extend_from_slice(&9999u32.to_le_bytes());
        w.extend_from_slice(&[0u8; 12]);
    } else {
        w.extend_from_slice(&0x0196u16.to_le_bytes());
        w.extend_from_slice(&efst.to_le_bytes());
        w.extend_from_slice(&u.id.to_le_bytes());
        w.push(0);
    }
    let target = if u.sc.option & unit::OPTION_INVISIBLE != 0 { Target::SelfOnly } else { Target::Area };
    clif::send(c, &w, u, target);
}

/// clif.cpp clif_parse_LoadEndAck (CZ_NOTIFY_ACTORINIT): the client finished loading the map.
pub fn load_end_ack(c: &Ctx, aid: u32) {
    let Some(u) = unit::get(c, aid) else { return };
    let Some(mut p) = get(c, aid) else { return };
    if u.on_map {
        return;
    }
    if !p.active || !p.pc_loaded {
        p.connect_new = false;
        put(c, &p);
        return;
    }
    if p.rewarp {
        p.rewarp = false;
        put(c, &p);
        clif::changemap(c, &u, u.map, u.x, u.y);
        return;
    }
    p.warping = false;
    put(c, &p);
    // look (PACKETVER >= 4: clif_changelook(sd, LOOK_WEAPON, 0)), pc_set_costume_view
    crate::pc_equip::clif_changelook(c, aid, crate::pc_equip::LOOK_WEAPON, 0);
    crate::pc_equip::pc_set_costume_view(c, aid);
    // clif_refresh_clothcolor sends nothing for PACKETVER >= 20091103
    // item: inventory list first, otherwise deleted items in pc_checkitem show up as 'unknown item'
    crate::clif_items::inventorylist(c, aid);
    crate::inventory::pc_checkitem(c, aid);
    crate::clif_items::equipswitch_list(c, aid);
    // cart
    if crate::inventory::pc_iscarton(c, aid) {
        crate::clif_items::cartlist(c, aid);
        crate::inventory::updatestatus(c, aid, crate::inventory::SP_CARTINFO);
    }
    // weight
    crate::inventory::updatestatus(c, aid, crate::inventory::SP_WEIGHT);
    crate::inventory::updatestatus(c, aid, crate::inventory::SP_MAXWEIGHT);
    // guild (needs to go before clif_spawn() to show guild emblems correctly)
    if crate::guild::guild_id_of(c, aid) != 0 {
        crate::guild::guild_send_memberinfoshort(c, aid, true);
    }
    let Some(u) = unit::get(c, aid) else { return };
    setinvincibletimer(c, aid);
    // clif.cpp:10805-10810: `if( mapdata->users++ == 0 && battle_config.dynamic_mobs ) map_spawnmobs(sd->m);` and
    // sd->state.debug_remove_map = 0 (the monster system keeps the map's user count)
    crate::mob::spawn::map_users_inc(c, aid, u.map);
    let Some(mut u) = unit::get(c, aid) else { return };
    // reset the callshop flag if the player changes map
    let mut x = crate::script::npc_run::sdx(c, aid);
    if x.callshop {
        x.callshop = false;
        crate::script::npc_run::put_sdx(c, &x);
    }
    if !unit::addblock(c, &mut u) {
        return;
    }
    crate::presence::on_enter_map(c, aid, u.map); // Loki's own: the floating headsets of this map are this player's from now on (presence.rs)
    crate::horde::on_enter_map(c, aid); // Loki's own: a Horde reward that waited for this character is given now (horde/reward.rs)
    clif::spawn(c, &u, false);
    if crate::party::party_id_of(c, aid) != 0 {
        crate::party::party_send_movemap(c, aid);
        if let Some(u) = unit::get(c, aid) {
            crate::party::clif_party_hp(c, &u); // Show hp after displacement [LuzZza]
        }
    }
    crate::clif_bg::load_end_ack_hp(c, aid); // BattleGround System (clif.cpp:10826)
    let m = u.map;
    // the PvP ranking registration before the map property (clif.cpp:10827-10835)
    crate::battle::pvp::load_end_ack(c, aid);
    let invisible = u.sc.option & unit::OPTION_INVISIBLE != 0;
    // (pvp, else a duel [LuzZza], else a gvg dungeon: the free-PvP property)
    let property = if (crate::npc::mapflag(c, m, crate::npc::MF_PVP) && !invisible) || crate::duel::duel_group(c, aid) != 0 || crate::npc::mapflag(c, m, crate::npc::MF_GVG_DUNGEON) {
        clif::MAPPROPERTY_FREEPVPZONE
    } else if crate::npc::mapflag_gvg(c, m) {
        clif::MAPPROPERTY_AGITZONE
    } else {
        clif::MAPPROPERTY_NOTHING
    };
    clif::map_property(c, &u, property, Target::SelfOnly);
    // info about nearby objects
    let r = crate::conf::area_size();
    for other in map::units_in_area(c, m, u.x - r, u.y - r, u.x + r, u.y + r, map::BL_ALL) {
        clif::getareachar(c, &u, &other);
    }
    // pet (clif.cpp:10852-10865, pet.rs); homunculus (clif.cpp:10867-10883, homunculus.rs); the mercenary
    // (clif.cpp:10884-10891, mercenary.rs) and the elemental (clif.cpp:10893-10902, elemental.rs)
    crate::pet::load_end_ack(c, aid);
    crate::homunculus::load_end_ack(c, aid);
    crate::mercenary::load_end_ack(c, aid);
    crate::elemental::load_end_ack(c, aid);
    let Some(p) = get(c, aid) else { return };
    if p.connect_new {
        // clif_skillinfoblock, clif_hotkeys_send(sd, 0) and (PACKETVER_RE_NUM >= 20190508) clif_hotkeys_send(sd, 1)
        crate::clif_skill::clif_skillinfoblock(c, aid);
        crate::hotkey::clif_hotkeys_send(c, aid, 0);
        crate::hotkey::clif_hotkeys_send(c, aid, 1);
        // EXP / status blocks:
        crate::status::load_end_ack_connect_new(c, aid);
        let u = unit::get(c, aid).unwrap_or(u);
        crate::social::friends_toggle_login(c, &u, true);
        // Don't trigger NPC event or opening vending/buyingstore will be failed
        if crate::autotrade::autotrade(c, aid) == 0 {
            crate::script::npc_run::npc_script_event(c, aid, crate::script::npc_run::NPCE_LOGIN);
        }
    } else {
        // For some reason the client "loses" these on warp/map-change.
        crate::status::load_end_ack_refresh(c, aid);
        // abort currently running script
        crate::script::npc_run::abort_on_map_change(c, aid);
    }
    if get(c, aid).map(|p| p.changemap).unwrap_or(false) {
        if let Some(u) = unit::get(c, aid) {
            clif_changemap_restore(c, &u);
        }
        let pmap = get(c, aid).map(|p| p.pmap).unwrap_or(0);
        crate::status::load_end_ack_changemap(c, aid, pmap);
        // Instances do not need their own channels (after status_calc_pc, clif.cpp:11105-11107)
        if let Some(u) = unit::get(c, aid) {
            crate::channel::on_map_change(c, u.id, u.map);
        }
        // pk mode message: with pk mode
    }
    // the guild notice (guild_notice_changemap 2: displays at end), then mail_clear(sd), then the Guild Aura Init
    // (clif.cpp:11112-11125)
    crate::guild::load_end_ack_notice(c, aid);
    crate::mail::mail_clear(c, aid);
    crate::guild::guildaura_init(c, aid);
    // show we have a vending (clif.cpp:11127-11130)
    crate::vending::load_end_ack(c, aid);
    // Don't trigger NPC event or opening vending/buyingstore will be failed: OnPCLoadMapEvent on maps with the
    // loadevent flag
    if crate::autotrade::autotrade(c, aid) == 0 && crate::npc::mapflag(c, m, crate::data::constant("mf_loadevent").unwrap_or(42) as i16) {
        crate::script::npc_run::npc_script_event(c, aid, crate::script::npc_run::NPCE_LOADMAP);
    }
    let Some(u) = unit::get(c, aid) else { return };
    // clif.cpp:11136-11137: SG_DEVIL's permanent blindness (Star Emperor, or a Star Gladiator at the max job level)
    {
        let class_ = crate::status::data::sd_get(c, aid).map(|s| s.class_).unwrap_or(0);
        let ch = character_of(c, aid);
        if crate::skill::checkskill(c, aid, crate::db::skills::SG_DEVIL) != 0
            && ((class_ & crate::db::jobs::MAPID_THIRDMASK) == crate::db::jobs::MAPID_STAR_EMPEROR
                || ch.map(|ch| crate::db::jobs::pc_is_maxjoblv(ch.class, ch.job_level as u32)).unwrap_or(false))
        {
            crate::status::sc_ext::clif_status_load(c, &u, crate::status::consts::EFST_DEVIL1, 1); //blindness [Komurka]
        }
    }
    if u.sc.opt2 != 0 {
        clif::changeoption(c, &u, u.sc.option, None); //Client loses these on warp (clif.cpp:11139-11140).
    }
    // (SC_MONSTER_TRANSFORM ends in GvG: with the transformation statuses)
    // weather effects of the map (mapflags snow / fog / sakura / leaves / clouds / fireworks)
    crate::npc::weather_check(c, &u);
    // NPC OnTouch area under the arrival cell
    if map::getcell(c, u.map, u.x, u.y, CellChk::ChkNpc) {
        crate::npc::touch_area_allnpc(c, aid, u.map, u.x, u.y);
    } else {
        clear_areanpc(c, aid);
    }
    // it broke at some point (e.g. during a crash), so make it visibly dead again
    if let (Some(u), Some(p)) = (unit::get(c, aid), get(c, aid)) {
        if p.dead_sit == 1 {
            clif::clearunit_area(c, &u, CLR_DEAD);
        } else {
            crate::skillunit::castend::skill_usave_trigger(c, aid); // clif.cpp:11167 (spawn_direction: no)
        }
    }
    // Trigger skill effects if you appear standing on them (clif.cpp:11170-11171)
    if get(c, aid).map(|p| p.invincible_timer == 0).unwrap_or(false) {
        crate::skillunit::skill_unit_move_id(c, aid, c.tick(), 1);
    }
    // the NPC quest markers of the map
    crate::quest::pc_show_questinfo_reinit(c, aid);
    crate::quest::pc_show_questinfo(c, aid);
    // PACKETVER >= 20150513: `if( sd->mail.inbox.unread ) clif_Mail_new(sd, 0, nullptr, nullptr)`
    crate::mail::load_end_ack(c, aid);
    if let Some(mut p) = get(c, aid) {
        p.connect_new = false;
        p.changemap = false;
        put(c, &p);
    }
}

/// The "restore information that gets lost on map-change" part of LoadEndAck.
fn clif_changemap_restore(c: &Ctx, u: &Unit) {
    let Some(ch) = character_of(c, u.id) else { return };
    // clif_partyinvitationstate: ZC_PARTY_CONFIG
    clif::send(c, &[0xc9, 0x02, ch.disable_partyinvite], u, Target::SelfOnly);
    // clif_equipcheckbox: ZC_CONFIG_NOTIFY
    clif::send(c, &[0xda, 0x02, ch.show_equip], u, Target::SelfOnly);
    // clif_pet_autofeed_status(sd, false) (pet_autofeed_always: yes)
    crate::pet::clif::clif_pet_autofeed_status(c, u.id, false);
    configuration(c, u, CONFIG_CALL, ch.disable_call != 0);
    // PACKETVER >= 20170920: homunculus_autofeed_always (clif.cpp:11023-11036, homunculus.rs)
    crate::homunculus::load_end_ack_autofeed(c, u);
    // (CONFIG_DISABLE_SHOWCOSTUMES only for PACKETVER >= 20230419)
    // clif_reputation_list (ZC_REPUTE_INFO, clif.cpp:11042)
    crate::reputation::clif_reputation_list(c, u.id);
    // guild notice (guild_notice_changemap 1), KS, HP meter: with their systems (the flee refresh, night, the
    // battleground map type and score, the status clear and recalculation, the map channel: load_end_ack)
}

/// clif.hpp e_config_type.
pub const CONFIG_OPEN_EQUIPMENT_WINDOW: u32 = 0;
pub const CONFIG_CALL: u32 = 1;
pub const CONFIG_PET_AUTOFEED: u32 = 2;
pub const CONFIG_HOMUNCULUS_AUTOFEED: u32 = 3;

/// clif.cpp clif_configuration: ZC_CONFIG (0x02d9) type.L enabled.L.
pub fn configuration(c: &Ctx, u: &Unit, kind: u32, enabled: bool) {
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x02d9u16.to_le_bytes());
    w.extend_from_slice(&kind.to_le_bytes());
    w.extend_from_slice(&(enabled as u32).to_le_bytes());
    clif::send(c, &w, u, Target::SelfOnly);
}

/// pc.cpp pc_setinvincibletimer.
pub fn setinvincibletimer(c: &Ctx, aid: u32) {
    let Some(mut p) = get(c, aid) else { return };
    let Some(u) = unit::get(c, aid) else { return };
    let val = match crate::npc::mapflag_value(c, u.map, crate::npc::MF_INVINCIBLE_TIME) {
        v if v > 0 => v as i64,
        _ => crate::conf::pc_invincible_time(),
    };
    if val <= 0 {
        return;
    }
    if p.invincible_timer != 0 {
        timer::delete(c, p.invincible_timer);
    }
    p.invincible_timer = timer::add(c, c.tick() + val, Func::PcInvincible, aid, 0);
    put(c, &p);
}

/// pc.cpp pc_delinvincibletimer.
pub fn delinvincibletimer(c: &Ctx, aid: u32) {
    let Some(mut p) = get(c, aid) else { return };
    if p.invincible_timer != 0 {
        timer::delete(c, p.invincible_timer);
        p.invincible_timer = 0;
        put(c, &p);
        crate::skillunit::skill_unit_move_id(c, aid, c.tick(), 1);
    }
}

/// pc.cpp pc_invincible_timer.
pub fn invincible_timer(c: &Ctx, tid: u64, aid: u32) {
    let Some(mut p) = get(c, aid) else { return };
    if p.invincible_timer != tid {
        log::error!("invincible_timer {} != {}", p.invincible_timer, tid);
        return;
    }
    p.invincible_timer = 0;
    put(c, &p);
    crate::skillunit::skill_unit_move_id(c, aid, c.tick(), 1);
}

/// unit_remove_map's player part (unit.cpp): leave chats, cancel trades and invitations, stand up.
pub fn on_remove_map(c: &Ctx, u: &mut Unit) {
    crate::presence::on_remove_map(c, u.id); // Loki's own: the floating headset and voice leave the map with the player (presence.rs)
    let aid = u.id;
    // unit.cpp:3634: if(sd->shadowform_id) { // If shadow target has leave the map (thief agent)
    let shadowform_id = crate::skill::state::get(c, aid).shadowform_id;
    if shadowform_id != 0 && crate::unit::get(c, shadowform_id).is_some() {
        crate::status::sc::status_change_end(c, shadowform_id, crate::status::consts::SC__SHADOWFORM, 0);
    }
    // Leave/reject all invitations.
    if get(c, aid).map(|p| p.chat_id != 0).unwrap_or(false) {
        crate::chat::chat_leavechat(c, aid, false);
    }
    if crate::trade::tx(c, aid).partner_id > 0 {
        crate::trade::trade_tradecancel(c, aid);
    }
    crate::searchstore::searchstore_close(c, aid);
    // unit.cpp:3646-3655: the storage is saved and force-closed (no packet) when the player leaves the map, except
    // while the Teleport menu is open (bugreport:8027: skill_castend_map reopens the storage window after the warp;
    // AL_TELEPORT's menu comes from skills/acolyte/teleport.rs through clif_skill_warppoint)
    if crate::itemui::menu(c, aid).0 == crate::db::skills::AL_TELEPORT as i32 {
        // (the storage stays open)
    } else if let Some(mut p) = get(c, aid) {
        match p.storage_flag {
            1 => crate::storage::storage_storage_quit(c, aid),
            2 => crate::guild_storage::storage_guild_storage_quit(c, aid, 0),
            3 => crate::storage::storage_premium_storage_quit(c, aid),
            _ => {}
        }
        if p.storage_flag != 0 {
            p = get(c, aid).unwrap_or(p);
            p.storage_flag = 0; // Force close it when being warped.
            put(c, &p);
        }
    }
    let invite = crate::social::sx(c, aid).party_invite;
    if invite > 0 {
        crate::party::party_reply_invite(c, aid, invite, 0);
    }
    // guild_reply_invite / guild_reply_reqalliance refuse a pending guild invitation / alliance request
    crate::guild::on_remove_map_invites(c, aid);
    // OnTouch_ goes to the next player in the area
    crate::script::npc_run::npc_touchnext_areanpc(c, aid, true);
    // Check if warping and not changing the map (unit.cpp:3673-3676).
    if get(c, aid).map(|p| p.warping && !p.changemap).unwrap_or(false) {
        crate::status::sc::status_change_end(c, aid, crate::status::consts::SC_CLOAKING, 0);
        crate::status::sc::status_change_end(c, aid, crate::status::consts::SC_CLOAKINGEXCEED, 0);
        if let Some(fresh) = unit::get(c, aid) {
            *u = fresh;
        }
    }
    // `if (sd->duel_group > 0) duel_leave(sd->duel_group, sd)` (unit.cpp:3687-3688, after the PvP timer and before
    // standing up: its packets go before those of pc_setstand)
    crate::duel::unit_remove_map(c, aid);
    if let Some(fresh) = unit::get(c, aid) {
        *u = fresh;
    }
    if u.vd.dead_sit == 2 && setstand(c, aid, false) {
        if let Some(fresh) = unit::get(c, aid) {
            *u = fresh;
        }
        crate::status::sc_ext::skill_sit(c, aid, false);
        if let Some(fresh) = unit::get(c, aid) {
            *u = fresh;
        }
    }
    // the PvP ranking timer stops (unit.cpp:3681-3685); it comes before the sit / party dot parts in rAthena
    crate::battle::pvp::remove_map(c, aid);
    crate::party::party_send_dot_remove(c, u); // minimap dot fix [Kevin]
    crate::guild::guild_send_dot_remove(c, u);
    crate::battleground::bg_send_dot_remove(c, aid); // unit.cpp:3695
    // unit.cpp:3697-3717: `--map[bl->m].users` (map_removemobs when it reaches 0), unless already removed
    let debug_remove_map = get(c, aid).map(|p| p.debug_remove_map).unwrap_or(false);
    crate::mob::spawn::map_users_dec(c, aid, u.map, debug_remove_map);
    if let Some(mut p) = get(c, aid) {
        p.npc_shopid = 0;
        // sd->areanpc stays (unit_remove_map_ does not clear it): the arrival's clif_parse_LoadEndAck prunes it in
        // npc_touch_area_allnpc or clears it, so a player respawned on the OnTouch cell it died on does not trigger
        // the area again
        p.debug_remove_map = true;
        put(c, &p);
    }
    // trades, pvp timers, duels: with their systems
}

/// clif.cpp clif_getareachar_pc (the player-specific extras when a player comes into view).
pub fn getareachar_pc(c: &Ctx, sd: &Unit, dstsd: &Unit) {
    // a chat room the player owns, else a vending shop's board, else a buying store's board
    crate::chat::getareachar_pc(c, dstsd);
    if get(c, dstsd.id).map(|p| p.chat_id == 0 && p.vending).unwrap_or(false) {
        crate::vending::getareachar_pc(c, sd, dstsd);
    } else if get(c, dstsd.id).map(|p| p.chat_id == 0 && !p.vending && p.buyingstore).unwrap_or(false) {
        crate::buyingstore::clif_buyingstore_entry(c, dstsd.id, Some(sd));
    }
    // spirit spheres, shields, charms: with their systems
    crate::skill::state::getareachar_pc_spheres(c, sd, dstsd);
    // Party-mate, or hpdisp setting; BattleGround
    let pid = crate::party::party_id_of(c, sd.id);
    if (pid != 0 && crate::party::party_id_of(c, dstsd.id) == pid)
        || crate::clif_bg::same_team(c, sd.id, dstsd.id)
        || has_permission(c, sd.id, crate::rules::PC_PERM_VIEW_HPMETER)
    {
        // dstsd->battle_status.hp / max_hp
        let (hp, max_hp) = crate::status::status_data_of(c, dstsd.id).map(|s| (s.hp, s.max_hp)).unwrap_or((dstsd.hp as u32, dstsd.max_hp as u32));
        crate::status::clif::hpmeter_single(c, sd, dstsd.id, hp, max_hp);
    }
    // devotion links: with the skills
    crate::skill::state::getareachar_pc_devotion(c, sd, dstsd);
}

/// clif_outsight's extras for a player going out of view (chat rooms, vending boards).
pub fn outsight_extras(c: &Ctx, bl: &Unit, tsd: &Unit) {
    crate::chat::outsight_pc(c, bl);
    crate::vending::outsight(c, bl, tsd);
    if get(c, bl.id).map(|p| p.buyingstore).unwrap_or(false) {
        crate::buyingstore::clif_buyingstore_disappear_entry(c, bl.id, Some(tsd));
    }
}

/// clif.cpp clif_getareachar_item (flooritem.rs).
pub fn getareachar_item(c: &Ctx, sd: &Unit, item: &Unit) {
    crate::flooritem::clif_getareachar_item(c, sd, item);
}

/// cloakonnpc state for this viewer (phase 5).
pub fn npc_cloaked_for(c: &Ctx, viewer: u32, npc: u32) -> bool {
    get(c, viewer).map(|p| p.cloaked_npc.contains(&npc)).unwrap_or(false)
}

/// clif_name's party / guild / position / title for a player.
pub fn name_parts(c: &Ctx, bl: &Unit) -> (String, String, String, u32) {
    let title = character_of(c, bl.id).map(|ch| ch.title_id).unwrap_or(0);
    let guild = crate::guild::name_parts(c, bl.id);
    // do not display party unless the player is also in a guild
    let party = if guild.is_some() || crate::conf::display_party_name() { crate::party::party_name_of(c, bl.id) } else { String::new() };
    // the guild's name and the player's position name, `else if (sd->clan)`: the clan's name as the position
    let (gname, pname) = match guild {
        Some(g) => g,
        None => (String::new(), crate::clan::clan_name_of(c, bl.id).unwrap_or_default()),
    };
    (party, gname, pname, title)
}

// ---------------------------------------------------------------------------------------------- leaving

/// map.cpp map_quit: the character leaves the world (saved, removed).
pub fn quit(c: &Ctx, aid: u32) {
    // `if (sd->state.keepshop == false)` Close vending/buyingstore; the autotrade timer (autotrade.rs, map.cpp:2224-2248)
    crate::autotrade::map_quit(c, aid);
    let Some(p) = get(c, aid) else { return };
    if !p.active {
        crate::quest::unit_free(c, aid);
        crate::skilltree::unit_free(c, aid);
        crate::atcmd_pc::unit_free(c, aid);
        // (the vending / autotrade / duel rows are the session's: none may outlive it)
        crate::vending::on_unit_free(c, aid);
        crate::autotrade::on_unit_free(c, aid);
        crate::duel::drop_state(c, aid);
        c.db().pc().account_id().delete(aid);
        c.db().unit().id().delete(aid);
        return;
    }
    // (the expiration timer, battlegrounds: with their systems; the shops and the autotrade timer: autotrade::map_quit above)
    // the player's NPC timer (OnTimerQuit) and the open dialog (npc_timerevent_quit, npc_event_dequeue), then
    // clan_member_left, pc_itemcd_do(sd, false), then OnPCLogoutEvent (map.cpp:2244-2264)
    crate::script::npc_run::on_quit(c, aid);
    if get(c, aid).is_none() {
        return;
    }
    // bg_team_leave(sd, true, true) / bg_queue_leave(sd, false) (map.cpp:2253-2257)
    crate::battleground::map_quit(c, aid);
    if get(c, aid).is_none() {
        return;
    }
    // `if (sd->status.clan_id) clan_member_left(*sd)` (map.cpp:2259-2260)
    crate::clan::map_quit(c, aid);
    crate::inventory::pc_itemcd_do(c, aid, false);
    crate::script::npc_run::npc_script_event(c, aid, crate::script::npc_run::NPCE_LOGOUT);
    if get(c, aid).is_none() {
        return;
    }
    // status changes that are not saved end
    crate::status::on_quit_before_save(c, aid);
    // equipment the character may not wear any more is taken off (map_quit)
    crate::inventory::map_quit_equip(c, aid);
    // Return loot to owner (map.cpp:2301-2304: pet_lootitem_drop)
    crate::pet::map_quit_loot(c, aid);
    // map.cpp:2306-2307: `if (sd->ed) elemental_clean_effect(sd->ed)` (elemental.rs)
    crate::elemental::map_quit(c, aid);
    // map.cpp:2311-2314: `if( mapdata->instance_id > 0 ) instance_delusers(mapdata->instance_id);`
    if let Some(u) = unit::get(c, aid) {
        crate::instance::map_quit(c, u.map);
    }
    // unit_remove_map_pc(sd, CLR_RESPAWN): the player, then the pet and the active homunculus (CLR_OUTSIGHT)
    unit::remove_map(c, aid, CLR_RESPAWN);
    crate::pet::unit_remove_map_pc(c, aid, CLR_RESPAWN);
    crate::homunculus::unit_remove_map_pc(c, aid, crate::unit::CLR_OUTSIGHT);
    // the mercenary and the elemental (unit.cpp:3878-3882, companion/unit.rs)
    crate::companion::unit::unit_remove_map_pc(c, aid, CLR_RESPAWN);
    crate::party::party_booking_delete(c, aid); // Party Booking [Spiria]
    makesavestatus(c, aid);
    // pc_clean_skilltree(sd), and chrif_save's skill slots as the char server stores them (skilltree.rs)
    crate::skilltree::map_quit(c, aid);
    crate::skill::state::pc_crimson_marker_clear(c, aid); // map.cpp:2327 (ninjags agent)
    crate::macro_detector::pc_macro_detector_disconnect(c, aid); // map.cpp:2328
    // chrif_save(CSAVE_QUIT | CSAVE_INVENTORY | CSAVE_CART): status changes and bonus scripts kept for the next
    // login (chrif_save_scdata, chrif_bsdata_save), then storage, inventory and cart are written back
    crate::status::on_quit_save(c, aid);
    // chrif_save(CSAVE_QUITTING): chrif_skillcooldown_save right after chrif_save_scdata (chrif.cpp:286-287)
    crate::skill::cooldown::chrif_skillcooldown_save(c, aid);
    crate::inventory::on_quit(c, aid);
    // (chrif_save's intif_save_petdata) the pet that is out
    crate::pet::chrif_save_pet(c, aid);
    // (chrif_save's hom_save) the active homunculus
    crate::homunculus::chrif_save(c, aid);
    // (chrif_save's mercenary_save / elemental_save) and char_mmo_char_tosql's mercenary_owner_tosql (mercenary.rs,
    // elemental.rs)
    crate::mercenary::chrif_save(c, aid);
    crate::elemental::chrif_save(c, aid);
    crate::mercenary::chrif_save_owner(c, aid);
    // (chrif_save's intif_quest_save) the quest log goes back to the char server's table
    crate::quest::chrif_save(c, aid);
    // (chrif_save's intif_achievement_save) and the achievement log
    crate::achievement::chrif_save(c, aid);
    // the char server's final-save handling (chmapif_parse_reqsavechar): char_set_char_offline → inter_guild_CharOffline
    if let Some(p) = get(c, aid) {
        crate::intif::send(crate::intif::Inter::GuildCharOffline { char_id: p.char_id });
    }
    // unit_free_pc (unit.cpp:3890-3904): the pet, the homunculus, the mercenary and the elemental are freed (saved,
    // or deleted) before the player (pet.rs, homunculus.rs, companion/unit.rs)
    crate::pet::unit_free_pc(c, aid);
    crate::homunculus::unit_free_pc(c, aid);
    crate::companion::unit::unit_free_pc(c, aid);
    crate::mercenary::on_pc_free(c, aid);
    if let Some(p) = get(c, aid) {
        if p.invincible_timer != 0 {
            timer::delete(c, p.invincible_timer);
        }
    }
    // unit_free: `if (sd->duel_invite > 0) duel_reject(sd->duel_invite, sd)` (duel.rs)
    crate::duel::unit_free(c, aid);
    // unit_free: channels, friends' notices, the party's member data
    crate::channel::channel_pcquit(c, aid, 0xF); // Leave all chan
    crate::skill::cooldown::skill_blockpc_clear(c, aid); // Clear all skill cooldown related (unit.cpp:3947)
    if let Some(u) = unit::get(c, aid) {
        crate::social::friends_toggle_login(c, &u, false); // Notify friends that this char logged out.
    }
    crate::party::party_send_logout(c, aid);
    crate::guild::guild_send_memberinfoshort(c, aid, false);
    // unit_free: pc_delspiritball(sd, sd->spiritball, 1), pc_delspiritcharm(sd, sd->spiritcharm, sd->spiritcharm_type)
    let sk = crate::skill::state::get(c, aid);
    crate::skill::state::pc_delspiritball(c, aid, sk.spiritball as i32, 1);
    crate::skill::state::pc_delspiritcharm(c, aid, sk.spiritcharm as i32, sk.spiritcharm_type);
    // unit_free: mob_removeslaves(sd) — the player's summoned monsters go with it
    if let Some(u) = unit::get(c, aid) {
        crate::mob::spawn::mob_removeslaves(c, &u);
    }
    // unit_free: event timers, a script still waiting for the player, the `@` variables
    crate::script::npc_run::on_unit_free(c, aid);
    // unit_free: skill_clear_unitgroup(bl) before status_change_clear (unit.cpp:4038); ud->skillunittick goes with
    // the unit_data (skillunit)
    crate::skillunit::skill_clear_unitgroup(c, aid);
    crate::skillunit::data::on_unit_freed(c, aid);
    // unit_free: autobonus, bonus scripts, status_change_clear(bl, 1), the status rows; the item session state
    crate::status::on_unit_free(c, aid);
    crate::battle::on_unit_free(c, aid);
    crate::inventory::on_unit_free(c, aid);
    crate::storage::on_unit_free(c, aid); // sd->premiumStorage
    crate::itemui::on_unit_free(c, aid);
    crate::quest::unit_free(c, aid);
    // unit_free: hat effects, progress bar, follow and the other script2 fields of the map_session_data
    crate::pc_misc::unit_free(c, aid);
    crate::atcmd_pc::unit_free(c, aid); // the @command fields of the map_session_data (atcmd agent)
    crate::skilltree::unit_free(c, aid);
    crate::achievement::achievement_free(c, aid); // unit_free: achievement_free (and sd->titles)
    crate::clan::unit_free(c, aid); // sd->clan
    crate::instance::unit_free(c, aid); // sd->instance_id / instance_mode
    crate::mail::on_unit_free(c, aid);
    crate::trade::on_unit_free(c, aid);
    crate::cashshop::on_unit_free(c, aid);
    crate::roulette::on_unit_free(c, aid); // sd->roulette / roulette_point
    crate::auction::on_unit_free(c, aid); // sd->auction
    crate::clif_client::unit_free(c, aid); // sd->state.lesseffect / inventory_expansion_*
    crate::buyingstore::on_unit_free(c, aid);
    crate::searchstore::on_unit_free(c, aid);
    crate::vending::on_unit_free(c, aid);
    crate::autotrade::on_unit_free(c, aid);
    // the skill rows of map_session_data / unit_data (skill fields, cooldown list, timerskills, shadow scars)
    crate::skill::state::free(c, aid);
    crate::pc_taekwon::free(c, aid);
    crate::skill::cooldown::free(c, aid);
    crate::skill::timerskill::free_unit(c, aid);
    crate::skill::unit::free_unit(c, aid);
    crate::battleground::unit_free(c, aid); // sd->bg_id / bg_queue_id / tid_queue_active
    // Every timer that still names this unit (walking, delayed walks) is dropped with it (unit_free).
    timer::delete_for_target(c, aid);
    c.db().pc().account_id().delete(aid);
    c.db().unit().id().delete(aid);
    crate::social::drop_sx(c, aid);
    crate::guild::drop_gx(c, aid);
    crate::timer::disarm_if_empty(c);
    log::info!("Character {aid} left the world.");
}

/// pc.cpp pc_makesavestatus: the last point is where the character stands (the save point when dead or on a
/// nosave map).
pub fn makesavestatus(c: &Ctx, aid: u32) {
    let Some(u) = unit::get(c, aid) else { return };
    let Some(p) = get(c, aid) else { return };
    let Some(mut ch) = c.db().character().id().find(p.char_id) else { return };
    // save_clothcolor: yes, save_body_style: yes (battle/client.conf): sd->status.clothes_color and body stay as they
    // are (the view data may differ, e.g. the body under a costume option)
    // NEW_CARTS (PACKETVER >= 20120201): INVISIBLE | FALCON | RIDING | DRAGON1-5 | WUG | WUGRIDER | MADOGEAR
    const SAVED_OPTIONS: i32 = 0x40 | 0x10 | 0x20 | 0x0788_0000 | 0x10_0000 | 0x20_0000 | 0x40_0000;
    ch.option = u.sc.option & SAVED_OPTIONS;
    // Mark last point as not instance related by default
    ch.last_instanceid = 0;
    let mapname = crate::data::maps().get(u.map).map(|d| d.name.clone()).unwrap_or_default();
    // HP / SP / AP: the battle values, or pc_setrestartvalue(sd, 0) when dead (SC_JAILED keeps the last point: phase 4)
    crate::status::makesavestatus_hp(c, aid, &mut ch, p.dead_sit == 1);
    if p.dead_sit == 1 {
        ch.last_map = ch.save_map.clone();
        ch.last_x = ch.save_x;
        ch.last_y = ch.save_y;
    } else if crate::npc::mapflag(c, u.map, crate::npc::MF_NOSAVE) {
        match crate::npc::mapflag_nosave_point(c, u.map) {
            Some((sm, sx, sy)) => {
                ch.last_map = sm;
                ch.last_x = sx;
                ch.last_y = sy;
            }
            None => {
                ch.last_map = ch.save_map.clone();
                ch.last_x = ch.save_x;
                ch.last_y = ch.save_y;
            }
        }
    } else if crate::instance::makesavestatus(c, &mut ch, u.map, u.x, u.y) {
        // If the user is on a instance map, special handling is needed (pc.cpp:1413-1428)
    } else {
        ch.last_map = mapname;
        ch.last_x = u.x as u16;
        ch.last_y = u.y as u16;
    }
    c.db().character().id().update(ch);
}

/// The client went away (its socket closed): set_eof, then clif_parse's eof branch (clif_quitsave: prevent_logout
/// keeps the character in the world for 10 s after combat, clif_delayquit) — crate::session.
pub fn on_client_gone(c: &Ctx, who: Identity) {
    crate::presence::on_client_gone(c, who); // Loki's own: the floating headset and voice go at once (presence.rs)
    crate::horde::on_client_gone(c, who); // Loki's own: the Horde link goes with the connection; a player in a run keeps its place a while (horde/service.rs)
    crate::session::connection_closed(c, who);
}

/// chrif.cpp chrif_disconnectplayer (the char server's mapif_disconnectplayer): the client is told why
/// (clif_authfail_fd: reason 1 server closed, 2 someone else logged in, 3 overpopulated, 4 time is up, 5 kicked by a
/// GM) and its connection is closed, so the character leaves through clif_quitsave. A character without a client (no
/// fd) is left to its clif_delayquit timer, unless it is an autotrader: that one leaves at once (map_quit).
pub fn disconnectplayer(c: &Ctx, aid: u32, why: u8) {
    let Some(p) = get(c, aid) else { return };
    // if (!sd->fd): a character without its client (an eof session clif_parse has not closed yet still has one)
    if p.disconnected && !crate::session::eof_pending(c, aid) {
        // `if (sd->state.autotrade) map_quit(sd);` — else the character waits for its clif_delayquit timer
        if crate::autotrade::autotrade(c, aid) != 0 {
            quit(c, aid);
        }
        return;
    }
    let kind = match why {
        1 => 1,
        2 => 2,
        3 => 4,
        4 => 10,
        5 => 15,
        _ => return,
    };
    // clif_authfail_fd: SC_NOTIFY_BAN, set_eof; clif_parse closes the session after this reducer's work
    crate::session::clif_authfail_fd(c, aid, kind);
}

/// clif.cpp clif_delayquit.
pub fn delayquit_timer(c: &Ctx, aid: u32) {
    if get(c, aid).map(|p| p.disconnected).unwrap_or(false) {
        quit(c, aid);
    }
}

// ---------------------------------------------------------------------------------------------- finding players

/// conf/battle/gm.conf `partial_name_scan: yes`.
fn partial_name_scan() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PARTIAL_NAME_SCAN) != 0
}

/// map.cpp map_nick2sd: a player by name (strcasecmp; with `allow_partial`, a unique case-insensitive prefix match,
/// or the exact name when several match). Every character in the world counts, including ones waiting to quit.
pub fn nick2sd(c: &Ctx, nick: &str, allow_partial: bool) -> Option<Unit> {
    let nick_b = crate::net::wire::latin1(nick);
    let mut found: Option<Unit> = None;
    let mut qty = 0;
    for p in c.db().pc().iter() {
        let Some(u) = unit::get(c, p.account_id) else { continue };
        let name_b = crate::net::wire::latin1(&u.name);
        if allow_partial && partial_name_scan() {
            if name_b.len() >= nick_b.len() && name_b[..nick_b.len()].eq_ignore_ascii_case(&nick_b) {
                let perfect = name_b == nick_b;
                found = Some(u);
                if perfect {
                    qty = 1;
                    break;
                }
                qty += 1;
            }
        } else if name_b.eq_ignore_ascii_case(&nick_b) {
            found = Some(u);
            qty = 1;
            break;
        }
    }
    if partial_name_scan() && qty != 1 {
        return None;
    }
    found
}

/// map.cpp map_charid2sd.
pub fn charid2sd(c: &Ctx, char_id: u32) -> Option<Unit> {
    let p = c.db().pc().char_id().find(char_id)?;
    unit::get(c, p.account_id)
}

/// pc_has_permission for an online character.
pub fn has_permission(c: &Ctx, aid: u32, perm: u32) -> bool {
    // sd->permissions: the group's, with permission_add / permission_remove applied (pc_misc.rs)
    crate::pc_misc::pc_has_permission(c, aid, perm)
}

/// pc_isdead.
pub fn isdead(c: &Ctx, aid: u32) -> bool {
    get(c, aid).map(|p| p.dead_sit == 1).unwrap_or(false)
}

/// pc.cpp pc_job_can_entermap.
pub fn job_can_entermap(c: &Ctx, jobid: i32, m: u16, group_lv: i32) -> bool {
    use crate::npc::*;
    if crate::data::maps().get(m).is_none() {
        return false;
    }
    if !clif::pcdb_checkid(jobid) {
        return false;
    }
    let Some(&(_, zone, lv)) = crate::data::job_noenter_map().iter().find(|j| j.0 == jobid) else { return true };
    if zone == 0 || group_lv > lv {
        return true;
    }
    let f = |flag| mapflag(c, m, flag);
    let vs2 = f(MF_PVP) || f(MF_GVG_DUNGEON) || f(MF_GVG) || f(MF_GVG_CASTLE) || f(MF_GVG_TE) || f(MF_GVG_TE_CASTLE) || f(MF_BATTLEGROUND);
    let map_zone = mapflag_value(c, m, MF_RESTRICTED);
    !((zone & 1 != 0 && !vs2)
        || (zone & 2 != 0 && f(MF_PVP))
        || (zone & 4 != 0 && (f(MF_GVG) || f(MF_GVG_CASTLE)))
        || (zone & 8 != 0 && f(MF_BATTLEGROUND))
        || (zone & 16 != 0 && (f(MF_GVG_TE) || f(MF_GVG_TE_CASTLE)))
        || (zone & map_zone != 0 && f(MF_RESTRICTED)))
}

/// pc.cpp pc_respawn: a dead character returns to its save point (a battleground revives its members at its cemetery).
pub fn respawn(c: &Ctx, aid: u32, clrtype: u8) {
    if !isdead(c, aid) {
        return;
    }
    if crate::battleground::bg_id_of(c, aid) != 0 && crate::battleground::bg_member_respawn(c, aid) {
        return; // member revived by battleground
    }
    setstand(c, aid, true);
    restart_value(c, aid, 3);
    let Some(ch) = character_of(c, aid) else { return };
    let ok = match crate::data::maps().id_of(&ch.save_map) {
        Some(m) => setpos(c, aid, m, ch.save_x as i16, ch.save_y as i16, clrtype) == SetPos::Ok,
        None => false,
    };
    if !ok {
        // If warping fails, send a normal stand up packet (clif_resurrection).
        if let Some(u) = unit::get(c, aid) {
            let p = crate::net::packets::PACKET_ZC_RESURRECTION { packetType: crate::net::packets::PACKET_ZC_RESURRECTION::ID as i16, gid: u.id, r#type: 0 };
            clif::send(c, &p.to_vec(), &u, Target::Area);
        }
    }
}
