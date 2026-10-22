//! clif.cpp's GM packets (rAthena e985006, PACKETVER 20211103): what the client's GM slash commands and its GM
//! right-click menu send. Most of them only build an "@command" line and hand it to is_atcommand(fd, sd, command, 1),
//! exactly as if the player had typed it — atcommand.rs then checks the group's right to the command, runs it, answers
//! and logs it: /b /nb (@kami), /lb /nlb (@lkami), /kill on a player (@kick), /killall (@kickall), /shift and /remove
//! (@jumpto), /recall and /summon (@recall), /item and /monster (@zeny, @item, @item2, @monster), /hide (@hide),
//! /resetcooltime (@resetcooltime), the manner points and /rc (@mute), /resetstate and /resetskill (@resetstat,
//! @resetskill), the equipment strip (@fullstrip), /changedress (@changedress), /setcamera and /viewpointvalue
//! (@camerainfo). The rest act on their own: /kill on a monster or an NPC (with their own permission checks), the
//! account name request, /changemaptype and /check.
//!
//! The old packets' fields sit at clif_packetdb.hpp's offsets for this PACKETVER (`packet_db[cmd].pos[]`: net/packets.rs
//! packet_pos); the newer ones are the generated structs. `safesnprintf(command, sizeof(command), …)` truncations are
//! kept (names are latin-1: one char per byte).

use crate::atcommand::ATCOMMAND_SYMBOL;
use crate::clif::{self, Target};
use crate::map::{BL_MOB, BL_NPC, BL_PC};
use crate::net::packets::{packet_pos, PACKET_CZ_BROADCAST, PACKET_CZ_LOCALBROADCAST, PACKET_CZ_RESET};
use crate::net::wire::{from_latin1, until_nul, Reader};
use crate::net::CHAT_SIZE_MAX;
use crate::world::Ctx;
use crate::{pc, rules, unit};

/// mmo.hpp NAME_LENGTH.
const NAME_LENGTH: usize = 24;

/// The GM packets' handlers, in clif.cpp's order; false when `func` is not one of them.
pub fn parse(c: &Ctx, aid: u32, func: &str, pkt: &[u8]) -> bool {
    match func {
        "clif_parse_Broadcast" => clif_parse_broadcast(c, aid, pkt),
        "clif_parse_ResetChar" => clif_parse_reset_char(c, aid, pkt),
        "clif_parse_LocalBroadcast" => clif_parse_local_broadcast(c, aid, pkt),
        "clif_parse_GMKick" => clif_parse_gm_kick(c, aid, pkt),
        "clif_parse_GMKickAll" => clif_parse_gm_kick_all(c, aid),
        "clif_parse_GMShift" => clif_parse_gm_shift(c, aid, pkt),
        "clif_parse_GMRemove2" => clif_parse_gm_remove2(c, aid, pkt),
        "clif_parse_GMRecall" => clif_parse_gm_recall(c, aid, pkt),
        "clif_parse_GMRecall2" => clif_parse_gm_recall2(c, aid, pkt),
        "clif_parse_GM_Item_Monster" => clif_parse_gm_item_monster(c, aid, pkt),
        "clif_parse_GMHide" => clif_parse_gm_hide(c, aid),
        "clif_parse_gm_resetcooltime" => clif_parse_gm_resetcooltime(c, aid),
        "clif_parse_GMReqNoChat" => clif_parse_gm_req_no_chat(c, aid, pkt),
        "clif_parse_GMRc" => clif_parse_gm_rc(c, aid, pkt),
        "clif_parse_GMReqAccountName" => clif_parse_gm_req_account_name(c, aid, pkt),
        "clif_parse_GMChangeMapType" => clif_parse_gm_change_map_type(c, aid, pkt),
        "clif_parse_Check" => clif_parse_check(c, aid, pkt),
        "clif_parse_GMFullStrip" => clif_parse_gm_full_strip(c, aid, pkt),
        "clif_parse_changedress" => clif_parse_changedress(c, aid),
        "clif_parse_camerainfo" => clif_parse_camerainfo(c, aid, pkt),
        _ => return false,
    }
    true
}

// ---------------------------------------------------------------------------------------------- helpers

/// is_atcommand(fd, sd, command, 1): the line runs as the player's own typed command (rAthena ignores the result).
fn atcommand(c: &Ctx, aid: u32, command: &str) {
    crate::atcommand::is_atcommand(c, aid, command, 1);
}

/// safesnprintf into a `size`-byte buffer: at most size - 1 bytes of the text.
fn bounded(size: usize, s: String) -> String {
    s.chars().take(size.saturating_sub(1)).collect()
}

/// The offset of the packet's field `i` (packet_db[RFIFOW(fd,0)].pos[i]).
fn pos(pkt: &[u8], i: usize) -> usize {
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    packet_pos(cmd).get(i).copied().unwrap_or(2) as usize
}

/// RFIFOCP(fd, off) read as a C string of at most `n` bytes (`name[n] = '\0'`, or safestrncpy into n + 1 bytes).
/// rAthena would read on past the packet when there is no NUL in it; here the string ends with the packet.
fn cstr(pkt: &[u8], off: usize, n: usize) -> String {
    let b = pkt.get(off..).unwrap_or(&[]);
    from_latin1(until_nul(&b[..b.len().min(n)]))
}

/// map.cpp map_id2sd (an id <= 0 is nobody): the online player's status.name.
fn map_id2sd_name(c: &Ctx, id: i32) -> Option<String> {
    if id <= 0 {
        return None;
    }
    pc::character_of(c, id as u32).map(|ch| ch.name)
}

fn group_id(c: &Ctx, aid: u32) -> u8 {
    pc::get(c, aid).map(|p| p.group_id).unwrap_or(0)
}

// ---------------------------------------------------------------------------------------------- broadcasts

/// clif.cpp clif_parse_Broadcast (CZ_BROADCAST 0x0099: /b, /nb): "@kami <message>".
fn clif_parse_broadcast(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_BROADCAST::read(&mut Reader::new(pkt)) else { return };
    if (p.packetSize as usize) < PACKET_CZ_BROADCAST::SIZE {
        return;
    }
    atcommand(c, aid, &broadcast_command(&p.message, p.packetSize));
}

/// clif_parse_Broadcast's line: safestrncpy(message, p->message, min(sizeof(message), packetSize - sizeof(*p) + 1))
/// (the whole message, up to its NUL, at most CHAT_SIZE_MAX - 1 bytes), then "%ckami %s" into CHAT_SIZE_MAX bytes.
fn broadcast_command(message: &[u8], packet_size: u16) -> String {
    let n = CHAT_SIZE_MAX.min(packet_size as usize - PACKET_CZ_BROADCAST::SIZE + 1);
    let msg = from_latin1(until_nul(&message[..message.len().min(n - 1)]));
    bounded(CHAT_SIZE_MAX, format!("{ATCOMMAND_SYMBOL}kami {msg}"))
}

/// clif.cpp clif_parse_LocalBroadcast (CZ_LOCALBROADCAST 0x019c: /lb, /nlb): "@lkami <message>".
fn clif_parse_local_broadcast(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_LOCALBROADCAST::read(&mut Reader::new(pkt)) else { return };
    atcommand(c, aid, &local_broadcast_command(&p.message, p.packetSize));
}

/// clif_parse_LocalBroadcast's line: safestrncpy(msg, p->message, min(len, sizeof(msg))) with len = packetSize - 4,
/// i.e. one byte less than the message (the client's NUL). With len 0 safestrncpy writes nothing and rAthena formats
/// its uninitialised buffer: here the message is empty. Then "%clkami %s" into CHAT_SIZE_MAX bytes.
fn local_broadcast_command(message: &[u8], packet_size: u16) -> String {
    let len = (packet_size as usize).saturating_sub(PACKET_CZ_LOCALBROADCAST::SIZE);
    let n = len.min(CHAT_SIZE_MAX);
    let msg = if n == 0 { String::new() } else { from_latin1(until_nul(&message[..message.len().min(n - 1)])) };
    bounded(CHAT_SIZE_MAX, format!("{ATCOMMAND_SYMBOL}lkami {msg}"))
}

// ---------------------------------------------------------------------------------------------- resets

/// clif.cpp clif_parse_ResetChar (CZ_RESET 0x0197: /resetstate, /resetskill).
fn clif_parse_reset_char(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_RESET::read(&mut Reader::new(pkt)) else { return };
    if let Some(cmd) = reset_command(p.r#type) {
        atcommand(c, aid, &cmd);
    }
}

/// clif_parse_ResetChar's line: type 0 "@resetstat", 1 "@resetskill" (the case PACKETVER_MAIN_NUM >= 20220216 drops
/// is compiled in for this client), anything else nothing.
fn reset_command(kind: u16) -> Option<String> {
    match kind {
        0 => Some(format!("{ATCOMMAND_SYMBOL}resetstat")),
        1 => Some(format!("{ATCOMMAND_SYMBOL}resetskill")),
        _ => None,
    }
}

/// clif.cpp clif_parse_gm_resetcooltime (CZ_CMD_RESETCOOLTIME 0x0a88: /resetcooltime; PACKETVER_RE_NUM >= 20160622).
fn clif_parse_gm_resetcooltime(c: &Ctx, aid: u32) {
    atcommand(c, aid, &bounded(CHAT_SIZE_MAX, format!("{ATCOMMAND_SYMBOL}resetcooltime")));
}

// ---------------------------------------------------------------------------------------------- kicks

/// clif.cpp clif_parse_GMKick (CZ_DISCONNECT_CHARACTER 0x00cc: /kill, and the GM right-click menu's "force to quit").
/// A player is kicked through "@kick <name>"; a monster is killed and an NPC unloaded here, each after its own
/// permission check (@killmonster's, @unloadnpc's); anything else (or nothing) gets the kick ack 0.
fn clif_parse_gm_kick(c: &Ctx, aid: u32, pkt: &[u8]) {
    let tid = Reader::at(pkt, pos(pkt, 0)).i32().unwrap_or(0);
    // map_id2bl: id_db (warp NPCs are not in it; floating NPCs are)
    let Some(target) = crate::script::buildins::script2::unitctl::map_id2bl(c, tid as u32) else {
        crate::session::clif_gm_kickack(c, aid, 0);
        return;
    };
    match target.kind() {
        BL_PC => {
            let Some(u) = target.unit() else { return };
            // status_get_name: the player's fakename when set
            let name = crate::skills::merchant::status_get_name(c, u);
            atcommand(c, aid, &bounded(NAME_LENGTH + 6, format!("{ATCOMMAND_SYMBOL}kick {name}")));
        }
        // This one does not invoke any atcommand, so we need to check for permissions.
        BL_MOB => {
            if !rules::can_use_command(group_id(c, aid), "killmonster", false) {
                crate::session::clif_gm_kickack(c, aid, 0);
                return;
            }
            let Some(u) = target.unit() else { return };
            // status_get_name (md->name), status_get_class (a monster's view class: vd->look[LOOK_BASE])
            let command = bounded(100, format!("/kick {} ({})", crate::skills::merchant::status_get_name(c, u), u.vd.class));
            crate::map_log::log_atcommand(c, aid, &command);
            let sd = unit::get(c, aid);
            crate::status::hp::status_percent_damage(c, sd.as_ref(), u.id, 100, 0, 0, true); // can invalidate 'target'
        }
        BL_NPC => {
            if rules::can_use_command(group_id(c, aid), "unloadnpc", false) {
                let id = target.id();
                crate::npc_load::npc_unload_duplicates(c, id);
                crate::npc::unload(c, id);
                crate::script::npc_run::npc_read_event_script(c);
            }
        }
        _ => crate::session::clif_gm_kickack(c, aid, 0),
    }
}

/// clif.cpp clif_parse_GMKickAll (CZ_DISCONNECT_ALL_CHARACTER 0x00ce: /killall): "@kickall" (a 15-byte buffer).
fn clif_parse_gm_kick_all(c: &Ctx, aid: u32) {
    atcommand(c, aid, &bounded(15, format!("{ATCOMMAND_SYMBOL}kickall")));
}

// ---------------------------------------------------------------------------------------------- warps

/// clif.cpp clif_parse_GMShift (CZ_REMOVE_AID 0x01ba: /remove, CZ_SHIFT 0x01bb: /shift): "@jumpto <name>" (the name
/// field cut at NAME_LENGTH - 1; "FIXME: remove is supposed to receive account name for clients prior 20100803RE").
fn clif_parse_gm_shift(c: &Ctx, aid: u32, pkt: &[u8]) {
    let name = cstr(pkt, pos(pkt, 0), NAME_LENGTH - 1);
    atcommand(c, aid, &bounded(NAME_LENGTH + 8, format!("{ATCOMMAND_SYMBOL}jumpto {name}")));
}

/// clif.cpp clif_parse_GMRemove2 (CZ_REMOVE_AID_SSO 0x0843: /remove by account id): "@jumpto <name>" when that
/// account's character is online.
fn clif_parse_gm_remove2(c: &Ctx, aid: u32, pkt: &[u8]) {
    let account_id = Reader::at(pkt, pos(pkt, 0)).u32().unwrap_or(0);
    if let Some(name) = map_id2sd_name(c, account_id as i32) {
        atcommand(c, aid, &bounded(NAME_LENGTH + 8, format!("{ATCOMMAND_SYMBOL}jumpto {name}")));
    }
}

/// clif.cpp clif_parse_GMRecall (CZ_RECALL 0x01bc: /recall, CZ_RECALL_GID 0x01bd: /summon): "@recall <name>" ("FIXME:
/// recall is supposed to receive account name for clients prior 20100803RE").
fn clif_parse_gm_recall(c: &Ctx, aid: u32, pkt: &[u8]) {
    let name = cstr(pkt, pos(pkt, 0), NAME_LENGTH - 1);
    atcommand(c, aid, &bounded(NAME_LENGTH + 8, format!("{ATCOMMAND_SYMBOL}recall {name}")));
}

/// clif.cpp clif_parse_GMRecall2 (CZ_RECALL_SSO 0x0842: /recall by account id): "@recall <name>" when that account's
/// character is online.
fn clif_parse_gm_recall2(c: &Ctx, aid: u32, pkt: &[u8]) {
    let account_id = Reader::at(pkt, pos(pkt, 0)).u32().unwrap_or(0);
    if let Some(name) = map_id2sd_name(c, account_id as i32) {
        atcommand(c, aid, &bounded(NAME_LENGTH + 8, format!("{ATCOMMAND_SYMBOL}recall {name}")));
    }
}

// ---------------------------------------------------------------------------------------------- /item, /monster

/// What clif_parse_GM_Item_Monster makes of its name before the monster search.
#[derive(Debug, PartialEq)]
enum ItemMonster {
    /// Nothing happens: an empty name, or SPPOINT / JOBLEVEL ("TODO /sp sends these values").
    Nothing,
    /// "money", or an item's AegisName / name: the line to run.
    Command(String),
    /// No such item: the monster search, with the trimmed name.
    Monster(String),
}

/// clif_parse_GM_Item_Monster up to the monster search. `raw` is the name field (24 bytes in 0x013f, 100 in 0x09ce).
/// The SPPOINT / JOBLEVEL test (strcmpi) sees the name before trim(); "money" and the item search see it trimmed.
fn item_monster(raw: &[u8]) -> ItemMonster {
    let s = until_nul(raw);
    if s.is_empty() {
        return ItemMonster::Nothing;
    }
    if s.eq_ignore_ascii_case(b"SPPOINT") || s.eq_ignore_ascii_case(b"JOBLEVEL") {
        return ItemMonster::Nothing; // rAthena: "TODO /sp sends these values"
    }
    let name = from_latin1(trim(s));
    // Zeny
    if name.eq_ignore_ascii_case("money") {
        return ItemMonster::Command(format!("{ATCOMMAND_SYMBOL}zeny {}", i32::MAX));
    }
    // Item (item_db.searchname: the AegisName, then the name, both case-insensitive)
    if let Some(id) = crate::db::items::itemdb_searchname(&name) {
        let cmd = if !crate::db::items::itemdb_isstackable2(id) {
            // Nonstackable
            format!("{ATCOMMAND_SYMBOL}item2 {} 1 0 0 0 0 0 0 0", id.nameid)
        } else if id.flag.guid {
            format!("{ATCOMMAND_SYMBOL}item {} 1", id.nameid)
        } else {
            format!("{ATCOMMAND_SYMBOL}item {} 20", id.nameid)
        };
        return ItemMonster::Command(cmd);
    }
    ItemMonster::Monster(name)
}

/// strlib.cpp trim: the ISSPACE characters at both ends go.
fn trim(b: &[u8]) -> &[u8] {
    let start = b.iter().position(|&x| !crate::cstr::is_space(x)).unwrap_or(b.len());
    let end = b.iter().rposition(|&x| !crate::cstr::is_space(x)).map(|e| e + 1).unwrap_or(start);
    if end <= start {
        &[]
    } else {
        &b[start..end]
    }
}

/// clif_parse_GM_Item_Monster's monster: the exact AegisName first (official behavior), else (rAthena's addition) a
/// monster id — strtoint32def, 0 when the name is not a number — or, when that is no valid monster, the name search
/// (`searchname`: mobdb_searchname).
fn monster_of(name: &str, searchname: impl Fn(&str) -> u16) -> Option<&'static crate::db::mobs::MobDb> {
    use crate::db::mobs;
    if let Some(mob) = mobs::mobdb_search_aegisname(name) {
        return Some(mob);
    }
    let mob_id = crate::mob::cmd::strtoint32def(name).unwrap_or(0);
    if mob_id == 0 || mobs::mobdb_checkid(mob_id) == 0 {
        match searchname(name) {
            0 => None,
            id => mobs::mob_db().find(id as u32),
        }
    } else {
        mobs::mob_db().find(mob_id as u32)
    }
}

/// clif.cpp clif_parse_GM_Item_Monster (CZ_ITEM_CREATE 0x013f, CZ_ITEM_CREATE_EX 0x09ce: /item, /monster): "money"
/// gives INT_MAX zeny (@zeny), an item name gives the item (@item2 for equipment, @item x1 for a GUID item, x20 for
/// the rest), else a monster name or id spawns it (@monster with the AegisName) when the player may use @monster.
fn clif_parse_gm_item_monster(c: &Ctx, aid: u32, pkt: &[u8]) {
    let raw = pkt.get(pos(pkt, 0)..).unwrap_or(&[]);
    match item_monster(raw) {
        ItemMonster::Nothing => {}
        ItemMonster::Command(cmd) => atcommand(c, aid, &cmd),
        ItemMonster::Monster(name) => {
            // Monster
            if rules::can_use_command(group_id(c, aid), "monster", false) {
                // mobdb_searchname in mob_db's current order (@reloadmobdb changes it: atcommand_admin.rs)
                if let Some(mob) = monster_of(&name, |n| crate::atcommand_atcmd::admin::mobdb_searchname(c, n)) {
                    // Call corresponding atcommand when a valid monster was found
                    atcommand(c, aid, &bounded(CHAT_SIZE_MAX, format!("{ATCOMMAND_SYMBOL}monster {}", mob.sprite)));
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------- hide, mute

/// clif.cpp clif_parse_GMHide (CZ_CHANGE_EFFECTSTATE 0x019d: /hide): "@hide" (the effect state is not read).
fn clif_parse_gm_hide(c: &Ctx, aid: u32) {
    atcommand(c, aid, &bounded(6, format!("{ATCOMMAND_SYMBOL}hide")));
}

/// clif.cpp clif_parse_GMReqNoChat (CZ_REQ_GIVE_MANNER_POINT 0x0149: the manner points of the right-click menu):
/// type 0 gives -value, 1 +value (so does any other type but 2), 2 (self mute) does nothing ("Disabled because
/// clients keep self-muting when you give players public @ commands"); "@mute <value> <name>" for an online target.
fn clif_parse_gm_req_no_chat(c: &Ctx, aid: u32, pkt: &[u8]) {
    let id = Reader::at(pkt, pos(pkt, 0)).i32().unwrap_or(0);
    let kind = Reader::at(pkt, pos(pkt, 1)).u8().unwrap_or(0);
    let mut value = Reader::at(pkt, pos(pkt, 2)).u16().unwrap_or(0) as i32;
    if kind == 0 {
        value = -value;
    }
    //If type is 2 and the ids don't match, this is a crafted hacked packet!
    if kind == 2 {
        return;
    }
    let Some(name) = map_id2sd_name(c, id) else { return };
    atcommand(c, aid, &mute_command(value, &name));
}

/// "%cmute %d %s" into a NAME_LENGTH + 15 byte buffer (GMReqNoChat, GMRc).
fn mute_command(value: i32, name: &str) -> String {
    bounded(NAME_LENGTH + 15, format!("{ATCOMMAND_SYMBOL}mute {value} {name}"))
}

/// clif.cpp clif_parse_GMRc (CZ_REQ_GIVE_MANNER_BYNAME 0x0212: /rc): "@mute 60 <name>".
fn clif_parse_gm_rc(c: &Ctx, aid: u32, pkt: &[u8]) {
    let name = cstr(pkt, pos(pkt, 0), NAME_LENGTH - 1);
    atcommand(c, aid, &mute_command(60, &name));
}

// ---------------------------------------------------------------------------------------------- own actions

/// clif.cpp clif_parse_GMReqAccountName (CZ_REQ_ACCOUNTNAME 0x01df: the right-click menu): clif_account_name with an
/// empty name (the intif_request_accinfo lookup is commented out in rAthena: "TODO: Figure out how does this actually
/// work").
fn clif_parse_gm_req_account_name(c: &Ctx, aid: u32, pkt: &[u8]) {
    let account_id = Reader::at(pkt, pos(pkt, 0)).u32().unwrap_or(0);
    clif_account_name(c, aid, account_id, "");
}

/// clif.cpp clif_account_name: ZC_ACK_ACCOUNTNAME (0x01e0 <account id>.L <account name>.24B) on the GM's fd.
fn clif_account_name(c: &Ctx, aid: u32, account_id: u32, accname: &str) {
    let Some(sd) = unit::get(c, aid) else { return };
    let mut w = Vec::with_capacity(30);
    w.extend_from_slice(&0x01e0u16.to_le_bytes());
    w.extend_from_slice(&account_id.to_le_bytes());
    crate::net::wire::put_str(&mut w, accname, NAME_LENGTH);
    clif::send(c, &w, &sd, Target::SelfOnly);
}

/// clif.cpp clif_parse_GMChangeMapType (CZ_CHANGE_MAPTYPE 0x0198: /changemaptype <x> <y> <type>), for groups with
/// PC_PERM_USE_CHANGEMAPTYPE: the cell of the GM's map takes the gat type (map_setgatcell) and every player on the map
/// is told (clif_changemapcell ALL_SAMEMAP). The fields are words; map_setgatcell / clif_changemapcell take int16s.
/// "FIXME: once players leave the map, the client 'forgets' this information."
fn clif_parse_gm_change_map_type(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !pc::has_permission(c, aid, rules::PC_PERM_USE_CHANGEMAPTYPE) {
        return;
    }
    let x = Reader::at(pkt, pos(pkt, 0)).u16().unwrap_or(0) as i32;
    let y = Reader::at(pkt, pos(pkt, 1)).u16().unwrap_or(0) as i32;
    let kind = Reader::at(pkt, pos(pkt, 2)).u16().unwrap_or(0) as i32;
    let Some(sd) = unit::get(c, aid) else { return };
    crate::map_cell::setgatcell(c, sd.map, x as i16, y as i16, kind);
    // ALL_SAMEMAP from sd: the players whose map is the GM's (map_cell.rs's list without a target unit)
    crate::map_cell::clif_changemapcell(c, sd.map, x as i16, y as i16, kind as i16, None);
}

/// clif.cpp clif_parse_Check (CZ_REQ_STATUS_GM 0x0213: /check <name>), for groups with PC_PERM_USE_CHECK: the status
/// values of an online player (exact name, map_nick2sd(charname, false)) whose group level is not above the GM's.
fn clif_parse_check(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !pc::has_permission(c, aid, rules::PC_PERM_USE_CHECK) {
        return;
    }
    let charname = cstr(pkt, pos(pkt, 0), NAME_LENGTH - 1);
    let Some(pl) = pc::nick2sd(c, &charname, false) else { return };
    let level = |id: u32| rules::group_level(group_id(c, id));
    if level(aid) < level(pl.id) {
        return;
    }
    clif_check(c, aid, pl.id);
}

/// clif.cpp clif_check: ZC_ACK_STATUS_GM (0x0214, 42 bytes) with `pl`'s stats (capped at 255) and the status points
/// the next point of each costs, then the battle values as words, to the GM's fd.
fn clif_check(c: &Ctx, aid: u32, pl: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let (Some(ch), Some(st)) = (pc::character_of(c, pl), crate::status::data::sd_get(c, pl)) else { return };
    use crate::status::consts::{SP_AGI, SP_DEX, SP_INT, SP_LUK, SP_STR, SP_VIT};
    let need = |t: i32| crate::status::progress::pc_need_status_point(c, pl, t, 1) as u8;
    let b = &st.battle_status;
    let mut w = Vec::with_capacity(42);
    w.extend_from_slice(&0x0214u16.to_le_bytes());
    for (stat, sp) in [(ch.str, SP_STR), (ch.agi, SP_AGI), (ch.vit, SP_VIT), (ch.int, SP_INT), (ch.dex, SP_DEX), (ch.luk, SP_LUK)] {
        w.push(stat.min(u8::MAX as u16) as u8);
        w.push(need(sp));
    }
    let words: [i32; 14] = [
        b.batk + b.rhw.atk as i32 + b.lhw.atk as i32,
        b.rhw.atk2 as i32 + b.lhw.atk2 as i32,
        b.matk_max as i32,
        b.matk_min as i32,
        b.def as i32,
        b.def2 as i32,
        b.mdef as i32,
        b.mdef2 as i32,
        b.hit as i32,
        b.flee as i32,
        b.flee2 as i32 / 10,
        b.cri as i32 / 10,
        (2000 - b.amotion as i32) / 10, // aspd
        0,                              // FIXME: What is 'plusASPD' supposed to be? Maybe adelay?
    ];
    for v in words {
        w.extend_from_slice(&(v as u16).to_le_bytes());
    }
    clif::send(c, &w, &sd, Target::SelfOnly);
}

/// clif.cpp clif_parse_GMFullStrip (CZ_REQ_FULLSTRIP 0x07f5: the right-click menu's equipment strip): "@fullstrip
/// <account id>" (read at offset 2 as an int32, into a 30-byte buffer).
fn clif_parse_gm_full_strip(c: &Ctx, aid: u32, pkt: &[u8]) {
    let t_aid = Reader::at(pkt, 2).i32().unwrap_or(0);
    atcommand(c, aid, &bounded(30, format!("{ATCOMMAND_SYMBOL}fullstrip {t_aid}")));
}

// ---------------------------------------------------------------------------------------------- client settings

/// clif.cpp clif_parse_changedress (0x0ae8: /changedress, /nocosplay; PACKETVER >= 20180103): "@changedress".
fn clif_parse_changedress(c: &Ctx, aid: u32) {
    atcommand(c, aid, &bounded(CHAT_SIZE_MAX, format!("{ATCOMMAND_SYMBOL}changedress")));
}

/// clif.cpp clif_parse_camerainfo (CZ_CAMERA_INFO 0x0a77 <type>.B <range>.F <rotation>.F <latitude>.F: /viewpointvalue
/// sends type 1, /setcamera the values): "@camerainfo", or "@camerainfo <range> <rotation> <latitude>" ("%03.03f").
fn clif_parse_camerainfo(c: &Ctx, aid: u32, pkt: &[u8]) {
    atcommand(c, aid, &camerainfo_command(pkt));
}

fn camerainfo_command(pkt: &[u8]) -> String {
    // /viewpointvalue
    if pkt.get(2) == Some(&1) {
        return bounded(CHAT_SIZE_MAX, format!("{ATCOMMAND_SYMBOL}camerainfo"));
    }
    // /setcamera
    let f = |off: usize| Reader::at(pkt, off).f32().unwrap_or(0.0);
    bounded(CHAT_SIZE_MAX, format!("{ATCOMMAND_SYMBOL}camerainfo {} {} {}", c_03_03f(f(3)), c_03_03f(f(7)), c_03_03f(f(11))))
}

/// printf("%03.03f", (double)v): three decimals, rounded half to even on the exact binary value like glibc (so is
/// Rust's `{:.3}`); glibc writes NaN as "nan" / "-nan". The width 3 never pads: every result is longer.
fn c_03_03f(v: f32) -> String {
    if v.is_nan() {
        return if v.is_sign_negative() { "-nan".into() } else { "nan".into() };
    }
    format!("{:.3}", v as f64)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn pkt(cmd: u16, body: &[u8]) -> Vec<u8> {
        let mut v = cmd.to_le_bytes().to_vec();
        v.extend_from_slice(body);
        v
    }

    #[test]
    fn broadcast_lines() {
        // /b hello: the client sends the NUL too
        assert_eq!(broadcast_command(b"hello\0", 10), "@kami hello");
        // no NUL: the whole message
        assert_eq!(broadcast_command(b"hello", 9), "@kami hello");
        // /lb drops the last byte of the message (safestrncpy with len = packetSize - 4)
        assert_eq!(local_broadcast_command(b"hello\0", 10), "@lkami hello");
        assert_eq!(local_broadcast_command(b"hello", 9), "@lkami hell");
        assert_eq!(local_broadcast_command(b"", 4), "@lkami ");
        // CHAT_SIZE_MAX: the message keeps 255 bytes, the line 255 bytes in all
        let long = vec![b'a'; 400];
        let b = broadcast_command(&long, 404);
        assert_eq!(b.len(), 255);
        assert!(b.starts_with("@kami aaa"));
        assert_eq!(local_broadcast_command(&long, 404).len(), 255);
        // latin-1 bytes stay one char each
        assert_eq!(broadcast_command(&[0xe9, 0], 6), "@kami \u{e9}");
    }

    #[test]
    fn name_packets() {
        // 0x01bb CZ_SHIFT <char name>.24B at pos 2; the 24th byte is cut
        let mut name = [b'x'; 24];
        name[..4].copy_from_slice(b"Alex");
        let p = pkt(0x01bb, &name);
        assert_eq!(pos(&p, 0), 2);
        assert_eq!(cstr(&p, pos(&p, 0), NAME_LENGTH - 1), "Alexxxxxxxxxxxxxxxxxxxx");
        let mut short = [0u8; 24];
        short[..4].copy_from_slice(b"Alex");
        assert_eq!(cstr(&pkt(0x0212, &short), 2, NAME_LENGTH - 1), "Alex");
        // the command buffers hold every 23-byte name
        let n23 = "N".repeat(23);
        assert_eq!(bounded(NAME_LENGTH + 8, format!("@jumpto {n23}")).len(), 31);
        assert_eq!(bounded(NAME_LENGTH + 8, format!("@recall {n23}")).len(), 31);
        assert_eq!(bounded(NAME_LENGTH + 6, format!("@kick {n23}")).len(), 29);
        assert_eq!(mute_command(60, "Alex"), "@mute 60 Alex");
        assert_eq!(mute_command(-65535, &n23), format!("@mute -65535 {n23}"));
        // GMReqNoChat 0x0149 <account id>.L <type>.B <value>.W at 2, 6, 7
        let p = pkt(0x0149, &[0x80, 0x84, 0x1e, 0x00, 0, 5, 0]);
        assert_eq!((pos(&p, 0), pos(&p, 1), pos(&p, 2)), (2, 6, 7));
        assert_eq!(Reader::at(&p, pos(&p, 0)).i32(), Some(2_000_000));
        assert_eq!(Reader::at(&p, pos(&p, 2)).u16(), Some(5));
        // /hide, /killall and /fullstrip fit their small buffers
        assert_eq!(bounded(6, format!("{ATCOMMAND_SYMBOL}hide")), "@hide");
        assert_eq!(bounded(15, format!("{ATCOMMAND_SYMBOL}kickall")), "@kickall");
        assert_eq!(bounded(30, format!("{ATCOMMAND_SYMBOL}fullstrip {}", i32::MIN)), "@fullstrip -2147483648");
        assert_eq!(reset_command(0).as_deref(), Some("@resetstat"));
        assert_eq!(reset_command(1).as_deref(), Some("@resetskill"));
        assert_eq!(reset_command(2), None);
    }

    #[test]
    fn item_monster_names() {
        use ItemMonster::*;
        let field = |s: &str| {
            let mut b = vec![0u8; 24];
            b[..s.len()].copy_from_slice(s.as_bytes());
            b
        };
        assert_eq!(item_monster(&field("")), Nothing);
        assert_eq!(item_monster(&field("sppoint")), Nothing);
        assert_eq!(item_monster(&field("JobLevel")), Nothing);
        // the SPPOINT test comes before trim(): with a space it is a (failed) item / monster search
        assert_eq!(item_monster(&field(" SPPOINT")), Monster("SPPOINT".into()));
        assert_eq!(item_monster(&field("money")), Command("@zeny 2147483647".into()));
        assert_eq!(item_monster(&field("  MONEY \t")), Command("@zeny 2147483647".into()));
        // a stackable item (AegisName or name, any case): 20 of it
        assert_eq!(item_monster(&field("red_potion")), Command("@item 501 20".into()));
        assert_eq!(item_monster(&field("Red Potion")), Command("@item 501 20".into()));
        // equipment: @item2
        assert_eq!(item_monster(&field("Knife")), Command("@item2 1201 1 0 0 0 0 0 0 0".into()));
        // not an item
        assert_eq!(item_monster(&field(" Poring ")), Monster("Poring".into()));
        // 0x09ce's 100-byte field
        let mut long = vec![0u8; 100];
        long[..5].copy_from_slice(b"money");
        assert_eq!(item_monster(&long), Command("@zeny 2147483647".into()));
        assert_eq!(trim(b" \t\r\n"), b"");
        assert_eq!(trim(b"a b"), b"a b");
    }

    #[test]
    fn monster_lookup() {
        let none = |_: &str| 0u16;
        // the AegisName, any case
        assert_eq!(monster_of("poring", none).map(|m| m.sprite.as_str()), Some("PORING"));
        // an id
        assert_eq!(monster_of("1002", none).map(|m| m.sprite.as_str()), Some("PORING"));
        // not a valid id and no name match
        assert!(monster_of("0", none).is_none());
        assert!(monster_of("99999999", none).is_none());
        // the name search decides for names that are no AegisName
        assert_eq!(monster_of("Poring Name", |n| if n == "Poring Name" { 1002 } else { 0 }).map(|m| m.id), Some(1002));
    }

    #[test]
    fn camerainfo_lines() {
        let mut p = pkt(0x0a77, &[1]);
        p.extend_from_slice(&[0u8; 12]);
        assert_eq!(camerainfo_command(&p), "@camerainfo");
        let mut p = pkt(0x0a77, &[0]);
        for v in [300.0f32, -0.0625, 45.5] {
            p.extend_from_slice(&v.to_le_bytes());
        }
        assert_eq!(camerainfo_command(&p), "@camerainfo 300.000 -0.062 45.500");
        assert_eq!(c_03_03f(0.1875), "0.188");
        assert_eq!(c_03_03f(f32::NAN), "nan");
        assert_eq!(c_03_03f(-f32::NAN), "-nan");
        assert_eq!(c_03_03f(f32::NEG_INFINITY), "-inf");
    }
}
