//! clif.cpp's castle packets (PACKETVER_RE_NUM 20211103 >= 20190717): ZC_GUILD_AGIT_INFO (0x0b27, the castles of the
//! guild window), ZC_REQ_ACK_AGIT_INVESTMENT (0x0b2d) and ZC_REQ_ACK_MOVE_GUILD_AGIT (0x0b2e), and the client's
//! CZ_REQ_AGIT_INVESTMENT (0x0b2c) / CZ_REQ_MOVE_GUILD_AGIT (0x0b28) handlers (clif_guild.rs's dispatch calls them).

use super::{castle_guild_id, castle_rt, castles_in_order, find_by_clientid};
use crate::clif::{send, Target};
use crate::db::misc::castle::{GuildCastle, WOE_FIRST_EDITION, WOE_SECOND_EDITION, WOE_THIRD_EDITION};
use crate::net::packets::*;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// clif.hpp enum e_siege_teleport_result.
pub const SIEGE_TP_SUCCESS: i16 = 0;
pub const SIEGE_TP_NOT_ENOUGH_ZENY: i16 = 1;
pub const SIEGE_TP_INVALID_MODE: i16 = 2;

/// clif.cpp clif_guild_castle_list: ZC_GUILD_AGIT_INFO with the client ids of the castles the player's guild owns
/// (castle_db order, castles without a client id left out). Nothing when the guild owns no castle.
pub fn clif_guild_castle_list(c: &Ctx, sd: &Unit) {
    let Some(g) = crate::guild::sd_guild(c, sd.id) else { return };
    let castle_count = super::guild_checkcastles(c, g.guild.guild_id);
    if castle_count <= 0 {
        return;
    }
    let castle_list: Vec<i8> = castles_in_order()
        .into_iter()
        .filter(|gc| castle_guild_id(c, gc.castle_id) == g.guild.guild_id && gc.client_id != 0)
        .map(|gc| gc.client_id as i8)
        .collect();
    let p = PACKET_ZC_GUILD_AGIT_INFO {
        packetType: PACKET_ZC_GUILD_AGIT_INFO::ID as i16,
        packetLength: (PACKET_ZC_GUILD_AGIT_INFO::SIZE + castle_list.len()) as i16,
        castle_list,
    };
    send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_guild_castleinfo: ZC_REQ_ACK_AGIT_INVESTMENT, the castle's economy and defense (nothing for a castle
/// without a client id).
pub fn clif_guild_castleinfo(c: &Ctx, sd: &Unit, castle: &GuildCastle) {
    if castle.client_id == 0 {
        return;
    }
    let rt = castle_rt(c, castle.castle_id);
    let p = PACKET_ZC_REQ_ACK_AGIT_INVESTMENT {
        packetType: PACKET_ZC_REQ_ACK_AGIT_INVESTMENT::ID as i16,
        castle_id: castle.client_id as i8,
        economy: rt.data.economy,
        defense: rt.data.defense,
    };
    send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_guild_castle_teleport_res: ZC_REQ_ACK_MOVE_GUILD_AGIT.
pub fn clif_guild_castle_teleport_res(c: &Ctx, sd: &Unit, result: i16) {
    let p = PACKET_ZC_REQ_ACK_MOVE_GUILD_AGIT { packetType: PACKET_ZC_REQ_ACK_MOVE_GUILD_AGIT::ID as i16, result };
    send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// The castle_id byte of CZ_REQ_AGIT_INVESTMENT / CZ_REQ_MOVE_GUILD_AGIT: an int8 that find_by_clientid's uint16
/// parameter sign-extends.
fn client_id_of(pkt: &[u8]) -> u16 {
    pkt.get(2).map(|&b| b as i8 as i16 as u16).unwrap_or(0)
}

/// clif.cpp clif_parse_guild_castle_info_request (CZ_REQ_AGIT_INVESTMENT): the investment info of a castle the
/// player's guild owns.
pub fn clif_parse_guild_castle_info_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(g) = crate::guild::sd_guild(c, aid) else { return };
    let Some(gc) = find_by_clientid(client_id_of(pkt)) else { return };
    if castle_guild_id(c, gc.castle_id) != g.guild.guild_id {
        return;
    }
    clif_guild_castleinfo(c, &sd, gc);
}

/// clif.cpp clif_parse_guild_castle_teleport_request (CZ_REQ_MOVE_GUILD_AGIT): the warp to a castle the player's
/// guild owns, for the castle's zeny (its siege price while its War of Emperium edition runs). Refused silently from
/// a castle map or a map without teleport / warp; ZC_REQ_ACK_MOVE_GUILD_AGIT only tells a lack of zeny.
pub fn clif_parse_guild_castle_teleport_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(g) = crate::guild::sd_guild(c, aid) else { return };
    let Some(gc) = find_by_clientid(client_id_of(pkt)) else { return };
    if !gc.warp_enabled {
        return;
    }
    if castle_guild_id(c, gc.castle_id) != g.guild.guild_id {
        return;
    }
    let Some(sd) = unit::get(c, aid) else { return };
    let flag = |f| crate::npc::mapflag(c, sd.map, f);
    if flag(crate::npc::MF_GVG_CASTLE) || flag(crate::npc::MF_NOTELEPORT) || flag(crate::npc::MF_NOWARP) {
        return;
    }
    let mut zeny = gc.zeny;
    match gc.type_ {
        WOE_FIRST_EDITION => {
            if crate::guild::agit_flag(c) {
                zeny = gc.zeny_siege;
            }
        }
        WOE_SECOND_EDITION => {
            if crate::guild::agit2_flag(c) {
                zeny = gc.zeny_siege;
            }
        }
        WOE_THIRD_EDITION => {
            if crate::guild::agit3_flag(c) {
                zeny = gc.zeny_siege;
            }
        }
        _ => {}
    }
    // pc_payzeny(sd, zeny, LOG_TYPE_OTHER): the uint32 price as an int32 argument
    if zeny != 0 && crate::inventory::pc_payzeny(c, aid, zeny as i32 as i64, crate::map_log::LOG_TYPE_OTHER, 0) != 0 {
        clif_guild_castle_teleport_res(c, &sd, SIEGE_TP_NOT_ENOUGH_ZENY);
        return;
    }
    let Some(m) = crate::data::maps().id_of(&gc.map_name) else { return };
    crate::pc::setpos(c, aid, m, gc.warp_x as i16, gc.warp_y as i16, unit::CLR_OUTSIGHT);
}
