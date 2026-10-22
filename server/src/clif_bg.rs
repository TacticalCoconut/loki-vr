//! map/clif.cpp: the battleground packets (clif_bg_*, clif_sendbgemblem_*, the queue window's packets and parsers) and
//! clif_send's BG targets. The battleground system itself is battleground.rs.

use crate::battleground::{self as bg, BgTeam};
use crate::clif::{self, Target};
use crate::net::packets::*;
use crate::net::wire::{latin1, put_str};
use crate::pc::pc as _;
use spacetimedb::Table as _;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// NAME_LENGTH.
const NAME_LENGTH: usize = 24;

/// clif.hpp enum send_target: the BG ones.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BgTarget {
    Bg,
    BgWos,
    BgSamemap,
    BgSamemapWos,
    BgArea,
    BgAreaWos,
}

/// clif.cpp clif_send for the BG targets: `bl` must be a player of a team; its team's members receive (the active
/// sessions; _SAMEMAP on its map; _AREA around it; _WOS not itself).
pub fn send(c: &Ctx, buf: &[u8], bl: &Unit, target: BgTarget) {
    if bl.kind != crate::map::BL_PC {
        return;
    }
    let bg_id = bg::bg_id_of(c, bl.id);
    if bg_id <= 0 {
        return;
    }
    let Some(t) = bg::team(c, bg_id) else { return };
    let r = crate::conf::area_size();
    let (x0, y0, x1, y1) = (bl.x - r, bl.y - r, bl.x + r, bl.y + r);
    for m in &t.members {
        let Some(p) = crate::pc::get(c, m.aid) else { continue };
        if p.disconnected {
            continue; // session_isActive
        }
        if m.aid == bl.id && matches!(target, BgTarget::BgWos | BgTarget::BgSamemapWos | BgTarget::BgAreaWos) {
            continue;
        }
        let Some(sd) = unit::get(c, m.aid) else { continue };
        if !matches!(target, BgTarget::Bg | BgTarget::BgWos) && sd.map != bl.map {
            continue;
        }
        if matches!(target, BgTarget::BgArea | BgTarget::BgAreaWos) && (sd.x < x0 || sd.y < y0 || sd.x > x1 || sd.y > y1) {
            continue;
        }
        c.send(p.identity, buf);
    }
}

fn send_self(c: &Ctx, aid: u32, buf: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, buf, &sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_bg_hp: ZC_BATTLEFIELD_NOTIFY_HP2 (0x0a0e, PACKETVER >= 20140613) to the team around the player.
pub fn clif_bg_hp(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let (hp, max_hp) = crate::status::status_data_of(c, aid).map(|s| (s.hp, s.max_hp)).unwrap_or((0, 0));
    // sd->status.account_id; `if( sd->battle_status.max_hp > INT32_MAX )`: hp / (max_hp / 100) of 100
    let (h, mh) = if max_hp > i32::MAX as u32 { (hp / (max_hp / 100), 100) } else { (hp, max_hp) };
    let mut w = Vec::with_capacity(14);
    w.extend_from_slice(&0x0a0eu16.to_le_bytes());
    w.extend_from_slice(&aid.to_le_bytes());
    w.extend_from_slice(&h.to_le_bytes());
    w.extend_from_slice(&mh.to_le_bytes());
    send(c, &w, &sd, BgTarget::BgAreaWos);
}

/// clif.cpp clif_bg_xy: ZC_BATTLEFIELD_NOTIFY_POSITION (0x02df) to the team on the map.
pub fn clif_bg_xy(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let (name, class) = crate::pc::character_of(c, aid).map(|ch| (ch.name, ch.class)).unwrap_or_default();
    let mut w = Vec::with_capacity(36);
    w.extend_from_slice(&0x02dfu16.to_le_bytes());
    w.extend_from_slice(&aid.to_le_bytes());
    put_str(&mut w, &name, NAME_LENGTH);
    w.extend_from_slice(&class.to_le_bytes());
    w.extend_from_slice(&sd.x.to_le_bytes());
    w.extend_from_slice(&sd.y.to_le_bytes());
    send(c, &w, &sd, BgTarget::BgSamemapWos);
}

/// clif.cpp clif_bg_xy_remove: the minimap dot goes (-1, -1).
pub fn clif_bg_xy_remove(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let mut w = Vec::with_capacity(36);
    w.extend_from_slice(&0x02dfu16.to_le_bytes());
    w.extend_from_slice(&aid.to_le_bytes());
    w.extend_from_slice(&[0u8; NAME_LENGTH]);
    w.extend_from_slice(&0u16.to_le_bytes());
    w.extend_from_slice(&(-1i16).to_le_bytes());
    w.extend_from_slice(&(-1i16).to_le_bytes());
    send(c, &w, &sd, BgTarget::BgSamemapWos);
}

/// clif.cpp clif_bg_message: ZC_BATTLEFIELD_CHAT (0x02dc) to the whole team, through its first member. `mes` is the
/// text (up to its NUL), `len` the length rAthena's caller passes (the field is len + 1 bytes, capped at CHAT_SIZE_MAX).
pub fn clif_bg_message(c: &Ctx, t: &BgTeam, src_id: u32, name: &str, mes: &[u8], len: usize) {
    let Some(first) = bg::bg_getavailablesd(t) else { return };
    let Some(sd) = unit::get(c, first) else { return };
    // limit length
    let len = (len + 1).min(crate::net::CHAT_SIZE_MAX);
    let mut w = Vec::with_capacity(8 + NAME_LENGTH + len);
    w.extend_from_slice(&0x02dcu16.to_le_bytes());
    w.extend_from_slice(&((len + NAME_LENGTH + 8) as i16).to_le_bytes());
    w.extend_from_slice(&src_id.to_le_bytes());
    put_str(&mut w, name, NAME_LENGTH);
    // safestrncpy(buf + 32, mes, len): at most len - 1 bytes up to the NUL, then NULs
    let text: Vec<u8> = mes.iter().copied().take_while(|&b| b != 0).take(len.saturating_sub(1)).collect();
    let start = w.len();
    w.extend_from_slice(&text);
    w.resize(start + len, 0);
    send(c, &w, &sd, BgTarget::Bg);
}

/// clif.cpp clif_parse_BattleChat (CZ_BATTLEFIELD_CHAT 0x02db): battleground chat after /battlechat.
fn parse_battle_chat(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some((_name, _message, output)) = crate::parse::process_message(c, aid, pkt, false) else { return };
    let out = latin1(&output);
    bg::bg_send_message(c, aid, &out, out.len());
}

/// clif.cpp clif_bg_updatescore: ZC_BATTLEFIELD_NOTIFY_POINT (0x02de) to every player on the map (ALL_SAMEMAP).
pub fn clif_bg_updatescore(c: &Ctx, m: u16) {
    let (lion, eagle) = bg::score(c, m);
    let w = score_packet(lion, eagle);
    for p in c.db().pc().iter() {
        if p.disconnected {
            continue;
        }
        if unit::get(c, p.account_id).map(|u| u.map == m).unwrap_or(false) {
            c.send(p.identity, &w);
        }
    }
}

fn score_packet(lion: i16, eagle: i16) -> Vec<u8> {
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x02deu16.to_le_bytes());
    w.extend_from_slice(&lion.to_le_bytes());
    w.extend_from_slice(&eagle.to_le_bytes());
    w
}

/// clif.cpp clif_bg_updatescore_single.
pub fn clif_bg_updatescore_single(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let (lion, eagle) = bg::score(c, sd.map);
    send_self(c, aid, &score_packet(lion, eagle));
}

fn emblem_packet(c: &Ctx, sd: &Unit) -> Vec<u8> {
    let name = crate::pc::character_of(c, sd.id).map(|ch| ch.name).unwrap_or_default();
    let mut w = Vec::with_capacity(32);
    w.extend_from_slice(&0x02ddu16.to_le_bytes());
    w.extend_from_slice(&sd.id.to_le_bytes());
    put_str(&mut w, &name, NAME_LENGTH); // name don't show in screen.
    w.extend_from_slice(&(bg::bg_id_of(c, sd.id) as u16).to_le_bytes());
    w
}

/// clif.cpp clif_sendbgemblem_area: ZC_BATTLEFIELD_NOTIFY_CAMPINFO (0x02dd) to the area.
pub fn clif_sendbgemblem_area(c: &Ctx, sd: &Unit) {
    clif::send(c, &emblem_packet(c, sd), sd, Target::Area);
}

/// clif.cpp clif_sendbgemblem_single: `sd`'s camp to the player `to`.
pub fn clif_sendbgemblem_single(c: &Ctx, to: &Unit, sd: &Unit) {
    clif::send(c, &emblem_packet(c, sd), to, Target::SelfOnly);
}

/// clif.hpp e_map_type MAPTYPE_BATTLEFIELD.
pub const MAPTYPE_BATTLEFIELD: u16 = 19;

/// clif.cpp clif_map_type: ZC_NOTIFY_MAPPROPERTY2.
pub fn clif_map_type(c: &Ctx, aid: u32, kind: u16) {
    let p = PACKET_ZC_NOTIFY_MAPPROPERTY2 { packetType: PACKET_ZC_NOTIFY_MAPPROPERTY2::ID as i16, r#type: kind as i16 };
    send_self(c, aid, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- the queue window

/// clif.cpp clif_bg_queue_apply_result: ZC_ACK_ENTRY_QUEUE_APPLY (0x08d8).
pub fn clif_bg_queue_apply_result(c: &Ctx, result: u8, name: &str, aid: u32) {
    let p = packet_bgqueue_ack { PacketType: packet_bgqueue_ack::ID as i16, r#type: result, bg_name: name.to_string() };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_bg_queue_apply_notify: ZC_NOTIFY_ENTRY_QUEUE_APPLY (0x08d9) with the players in the queue.
pub fn clif_bg_queue_apply_notify(c: &Ctx, name: &str, aid: u32) {
    let Some(q) = bg::bg_search_queue(c, bg::bg_queue_id_of(c, aid)) else {
        log::error!("clif_bg_queue_apply_notify: Player is not in a battleground queue.");
        return;
    };
    let p = packet_bgqueue_notify_entry { PacketType: packet_bgqueue_notify_entry::ID as i16, name: name.to_string(), position: (q.teama.len() + q.teamb.len()) as i32 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_bg_queue_cancel_result: ZC_ACK_ENTRY_QUEUE_CANCEL (0x08db).
pub fn clif_bg_queue_cancel_result(c: &Ctx, success: bool, name: &str, aid: u32) {
    let p = packet_bgqueue_notice_delete { PacketType: packet_bgqueue_notice_delete::ID as i16, r#type: success as u8, bg_name: name.to_string() };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_bg_queue_lobby_notify: ZC_NOTIFY_LOBBY_ADMISSION (0x08df), the window asking to enter.
pub fn clif_bg_queue_lobby_notify(c: &Ctx, name: &str, aid: u32) {
    let p = packet_bgqueue_battlebegins { PacketType: packet_bgqueue_battlebegins::ID as i16, bg_name: name.to_string(), game_name: name.to_string() };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_bg_queue_ack_lobby: ZC_REPLY_ACK_LOBBY_ADMISSION (0x08e1), the gong of an accepted entry.
pub fn clif_bg_queue_ack_lobby(c: &Ctx, result: bool, name: &str, lobbyname: &str, aid: u32) {
    let p = packet_bgqueue_battlebegin_ack { PacketType: 0x08e1, result: result as u8, bg_name: name.to_string(), game_name: lobbyname.to_string() };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_bg_queue_entry_init: ZC_ENTRY_QUEUE_INIT (0x090e), the client forgets its queue windows.
pub fn clif_bg_queue_entry_init(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_ENTRY_QUEUE_INIT { packetType: PACKET_ZC_ENTRY_QUEUE_INIT::ID as i16 };
    send_self(c, aid, &p.to_vec());
}

/// safestrncpy(name, RFIFOCP(fd, off), NAME_LENGTH): at most 23 bytes, up to the first NUL.
fn name_at(pkt: &[u8], off: usize) -> String {
    let b = pkt.get(off..).unwrap_or(&[]);
    let n: Vec<u8> = b.iter().copied().take(NAME_LENGTH - 1).take_while(|&x| x != 0).collect();
    crate::net::wire::from_latin1(&n)
}

/// clif.cpp clif_parse_bg_queue_apply_request (CZ_REQ_ENTRY_QUEUE_APPLY 0x08d7: type 1 solo, 2 party, 4 guild).
fn parse_bg_queue_apply_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !bg::feature_bgqueue() {
        return;
    }
    let kind = i16::from_le_bytes([pkt.get(2).copied().unwrap_or(0), pkt.get(3).copied().unwrap_or(0)]);
    let name = name_at(pkt, 4);
    if bg::bg_queue_id_of(c, aid) > 0 {
        clif_bg_queue_apply_result(c, bg::BG_APPLY_DUPLICATE, &name, aid); // Duplicate application warning
    } else if kind == 1 {
        bg::bg_queue_join_solo(c, &name, aid); // Solo
    } else if kind == 2 {
        bg::bg_queue_join_party(c, &name, aid); // Party
    } else if kind == 4 {
        bg::bg_queue_join_guild(c, &name, aid); // Guild
    } else {
        let (n, cid) = crate::pc::character_of(c, aid).map(|ch| (ch.name, ch.id)).unwrap_or_default();
        log::warn!("clif_parse_bg_queue_apply_request: Received invalid queue type: {kind} from player {n} (AID:{aid} CID:{cid}).");
        clif_bg_queue_apply_result(c, bg::BG_APPLY_INVALID_APP, &name, aid); // Someone sent an invalid queue type packet
    }
}

/// clif.cpp clif_parse_bg_queue_cancel_request (CZ_REQ_ENTRY_QUEUE_CANCEL 0x08da).
fn parse_bg_queue_cancel_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !bg::feature_bgqueue() {
        return;
    }
    let qid = bg::bg_queue_id_of(c, aid);
    let success = if qid > 0 {
        let q = bg::bg_search_queue(c, qid);
        if q.map(|q| q.state == bg::QUEUE_STATE_SETUP_DELAY).unwrap_or(false) {
            return; // Make the cancel button do nothing if the entry window is open. Otherwise it'll crash the game when you click on both the queue status and entry status window.
        }
        bg::bg_queue_leave(c, aid, true)
    } else {
        let (n, cid) = crate::pc::character_of(c, aid).map(|ch| (ch.name, ch.id)).unwrap_or_default();
        log::warn!("clif_parse_bg_queue_cancel_request: Player trying to request leaving non-existent queue with name: {n} (AID:{aid} CID:{cid}).");
        false
    };
    let name = name_at(pkt, 2);
    clif_bg_queue_cancel_result(c, success, &name, aid);
}

/// clif.cpp clif_parse_bg_queue_lobby_reply (CZ_REPLY_LOBBY_ADMISSION 0x08e0: 1 accept, 2 decline).
fn parse_bg_queue_lobby_reply(c: &Ctx, aid: u32, pkt: &[u8]) {
    if bg::bg_queue_id_of(c, aid) > 0 {
        let result = pkt.get(2).copied().unwrap_or(0);
        if result == 1 {
            // Accept
            bg::bg_queue_on_accept_invite(c, aid);
        } else if result == 2 {
            // Decline
            bg::bg_queue_leave(c, aid, true);
            clif_bg_queue_entry_init(c, aid);
        }
    }
}

/// clif.cpp clif_parse_bg_queue_request_queue_number (CZ_REQ_ENTRY_QUEUE_RANKING 0x090a).
fn parse_bg_queue_request_queue_number(c: &Ctx, aid: u32, pkt: &[u8]) {
    let name = name_at(pkt, 2);
    clif_bg_queue_apply_notify(c, &name, aid);
}

/// The battleground parsers (net/mod.rs dispatch).
pub fn parse(c: &Ctx, aid: u32, func: &str, pkt: &[u8]) -> bool {
    match func {
        "clif_parse_BattleChat" => parse_battle_chat(c, aid, pkt),
        "clif_parse_bg_queue_apply_request" => parse_bg_queue_apply_request(c, aid, pkt),
        "clif_parse_bg_queue_cancel_request" => parse_bg_queue_cancel_request(c, aid, pkt),
        "clif_parse_bg_queue_lobby_reply" => parse_bg_queue_lobby_reply(c, aid, pkt),
        "clif_parse_bg_queue_request_queue_number" => parse_bg_queue_request_queue_number(c, aid, pkt),
        _ => return false,
    }
    true
}

// ---------------------------------------------------------------------------------------------- hooks (other files)

/// clif.cpp clif_spawn (clif.cpp:1722-1723): a member on a battleground map shows its camp to the area.
pub fn spawn_pc(c: &Ctx, sd: &Unit) {
    if bg::bg_id_of(c, sd.id) != 0 && crate::npc::mapflag(c, sd.map, crate::npc::MF_BATTLEGROUND) {
        clif_sendbgemblem_area(c, sd);
    }
}

/// clif.cpp clif_getareachar_unit (clif.cpp:5081-5082): a member on a battleground map shows its camp to `sd`.
pub fn getareachar_pc(c: &Ctx, sd: &Unit, tsd: &Unit) {
    if bg::bg_id_of(c, tsd.id) != 0 && crate::npc::mapflag(c, tsd.map, crate::npc::MF_BATTLEGROUND) {
        clif_sendbgemblem_single(c, sd, tsd);
    }
}

/// clif.cpp clif_getareachar_pc (clif.cpp:5010-5014): team mates see each other's HP bar.
pub fn same_team(c: &Ctx, sd: u32, dstsd: u32) -> bool {
    let b = bg::bg_id_of(c, sd);
    b != 0 && b == bg::bg_id_of(c, dstsd)
}

/// clif.cpp clif_parse_LoadEndAck (clif.cpp:10826): a member's HP bar to its team after the spawn.
pub fn load_end_ack_hp(c: &Ctx, aid: u32) {
    if bg::bg_id_of(c, aid) != 0 {
        clif_bg_hp(c, aid); // BattleGround System
    }
}

/// clif.cpp clif_parse_LoadEndAck (clif.cpp:11071-11076), on a map change: the battleground mode, and the score on
/// maps whose battleground flag is 2.
pub fn load_end_ack_changemap(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    if crate::npc::mapflag(c, sd.map, crate::npc::MF_BATTLEGROUND) {
        clif_map_type(c, aid, MAPTYPE_BATTLEFIELD); // Battleground Mode
        if crate::npc::mapflag_value(c, sd.map, crate::npc::MF_BATTLEGROUND) == 2 {
            clif_bg_updatescore_single(c, aid);
        }
    }
}

/// clif.cpp clif_update_hp (clif.cpp:3544-3545).
pub fn update_hp(c: &Ctx, aid: u32) {
    if bg::bg_id_of(c, aid) != 0 {
        clif_bg_hp(c, aid);
    }
}
