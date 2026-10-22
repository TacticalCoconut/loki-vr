//! map/clif.cpp's guild packets: clif_send's GUILD* targets, the clif_guild_* senders (the layouts of PACKETVER
//! 20211103 RENEWAL: 0x0b7b guild info, 0x0b7c expulsion list, 0x0b7d member list, 0x016c belong info, 0x014c
//! alliances, 0x0a82 / 0x0a83 expulsion / leave, 0x0b47 emblem change, 0x0afd position name…), the dispatch of the
//! castle packets (woe/clif.rs) and the clif_parse_Guild* handlers.

use crate::clif::{self, Target};
use crate::guild::{self, gx};
use crate::int_guild::*;
use crate::net::packets::*;
use crate::net::wire::{from_latin1, latin1, Reader};
use crate::pc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

// ------------------------------------------------------------------------------------------------ clif_send GUILD*

/// The GUILD* targets of clif.cpp clif_send.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum GTarget {
    Guild,
    GuildWos,
    GuildSamemap,
    GuildSamemapWos,
    GuildArea,
    GuildAreaWos,
    GuildNobg,
}

/// clif.cpp clif_send for the GUILD targets: `bl` (a player whose status.guild_id and sd->guild are set) is the
/// source; the guild's online members receive (enable_spy is off: no @guildspy relay).
pub fn send(c: &Ctx, buf: &[u8], bl: &Unit, target: GTarget) {
    if bl.kind != crate::map::BL_PC || guild::guild_id_of(c, bl.id) == 0 {
        return;
    }
    let Some(g) = guild::sd_guild(c, bl.id) else { return };
    let r = crate::conf::area_size();
    let (x0, y0, x1, y1) = (bl.x - r, bl.y - r, bl.x + r, bl.y + r);
    for i in 0..g.guild.max() {
        let aid = g.guild.member[i].sd;
        if aid == 0 {
            continue;
        }
        let Some(p) = pc::get(c, aid) else { continue };
        if p.disconnected {
            continue; // session_isActive
        }
        if matches!(target, GTarget::GuildNobg) && crate::battleground::bg_id_of(c, aid) != 0 {
            continue;
        }
        if aid == bl.id && matches!(target, GTarget::GuildWos | GTarget::GuildSamemapWos | GTarget::GuildAreaWos) {
            continue;
        }
        let Some(sd) = unit::get(c, aid) else { continue };
        if !matches!(target, GTarget::Guild | GTarget::GuildNobg | GTarget::GuildWos) && sd.map != bl.map {
            continue;
        }
        if matches!(target, GTarget::GuildArea | GTarget::GuildAreaWos) && (sd.x < x0 || sd.y < y0 || sd.x > x1 || sd.y > y1) {
            continue;
        }
        c.send(p.identity, buf);
    }
    // enable_spy: no (@guildspy relays nothing)
}

fn send_self(c: &Ctx, sd: &Unit, buf: &[u8]) {
    clif::send(c, buf, sd, Target::SelfOnly);
}

// ------------------------------------------------------------------------------------------------ senders

/// clif.cpp clif_guild_xy: ZC_NOTIFY_POSITION_TO_GUILDM to the members on the same map.
pub fn clif_guild_xy(c: &Ctx, sd: &Unit) {
    let p = PACKET_ZC_NOTIFY_POSITION_TO_GUILDM { packetType: PACKET_ZC_NOTIFY_POSITION_TO_GUILDM::ID as i16, aid: sd.id, xPos: sd.x, yPos: sd.y };
    send(c, &p.to_vec(), sd, GTarget::GuildSamemapWos);
}

/// clif.cpp clif_guild_xy_single: `sd`'s position to `tsd` (none for a battleground member).
pub fn clif_guild_xy_single(c: &Ctx, sd: &Unit, tsd: &Unit) {
    if crate::battleground::bg_id_of(c, sd.id) != 0 {
        return;
    }
    let p = PACKET_ZC_NOTIFY_POSITION_TO_GUILDM { packetType: PACKET_ZC_NOTIFY_POSITION_TO_GUILDM::ID as i16, aid: sd.id, xPos: sd.x, yPos: sd.y };
    clif::send(c, &p.to_vec(), tsd, Target::SelfOnly);
}

/// clif.cpp clif_guild_xy_remove: the minimap dot goes (-1, -1).
pub fn clif_guild_xy_remove(c: &Ctx, sd: &Unit) {
    let p = PACKET_ZC_NOTIFY_POSITION_TO_GUILDM { packetType: PACKET_ZC_NOTIFY_POSITION_TO_GUILDM::ID as i16, aid: sd.id, xPos: -1, yPos: -1 };
    send(c, &p.to_vec(), sd, GTarget::GuildSamemapWos);
}

/// clif.cpp clif_guild_castle_list: ZC_GUILD_AGIT_INFO with the client ids of the castles the guild owns (woe/clif.rs).
pub fn clif_guild_castle_list(c: &Ctx, sd: &Unit) {
    crate::woe::clif::clif_guild_castle_list(c, sd);
}

/// clif.cpp clif_guild_created: ZC_RESULT_MAKE_GUILD.
pub fn clif_guild_created(c: &Ctx, sd: &Unit, flag: i32) {
    let p = PACKET_ZC_RESULT_MAKE_GUILD { packetType: PACKET_ZC_RESULT_MAKE_GUILD::ID as i16, result: flag as u8 };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_belonginfo: ZC_UPDATE_GDID.
pub fn clif_guild_belonginfo(c: &Ctx, sd: &Unit) {
    let Some(g) = guild::sd_guild(c, sd.id) else { return };
    let position = guild::guild_getposition(c, sd.id);
    if position < 0 {
        return;
    }
    let p = PACKET_ZC_UPDATE_GDID {
        PacketType: PACKET_ZC_UPDATE_GDID::ID as i16,
        guildId: g.guild.guild_id as u32,
        emblemVersion: g.guild.emblem_id,
        mode: g.guild.position[position as usize].mode as u32,
        isMaster: gx(c, sd.id).gmaster_flag as u8,
        interSid: 0, // InterSID (unknown purpose)
        guildName: g.guild.name.clone(),
    };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_memberlogin_notice: ZC_UPDATE_CHARSTAT2 (0x01f2) — from the member (GUILD_WOS) when online,
/// otherwise through the first online member (GUILD) with gender / hair 0.
pub fn clif_guild_memberlogin_notice(c: &Ctx, g: &MmoGuild, idx: usize, flag: u32) {
    let m = &g.member[idx];
    let mut p = PACKET_ZC_UPDATE_CHARSTAT { packetType: PACKET_ZC_UPDATE_CHARSTAT::ID, aid: m.account_id, cid: m.char_id, status: flag, gender: 0, hairStyle: 0, hairColor: 0 };
    if m.sd != 0 {
        let Some(sd) = unit::get(c, m.sd) else { return };
        if let Some(ch) = pc::character_of(c, m.sd) {
            p.gender = ch.sex as u16;
            p.hairStyle = ch.hair;
            p.hairColor = ch.hair_color;
        }
        send(c, &p.to_vec(), &sd, GTarget::GuildWos);
    } else if let Some(sd) = guild::guild_getavailablesd(g).and_then(|a| unit::get(c, a)) {
        send(c, &p.to_vec(), &sd, GTarget::Guild);
    }
}

/// clif.cpp clif_guild_masterormember: ZC_ACK_GUILD_MENUINTERFACE (0xd7 for the master, 0x57 otherwise).
pub fn clif_guild_masterormember(c: &Ctx, sd: &Unit) {
    let p = PACKET_ZC_ACK_GUILD_MENUINTERFACE { packetType: PACKET_ZC_ACK_GUILD_MENUINTERFACE::ID as i16, menuFlag: if gx(c, sd.id).gmaster_flag { 0xd7 } else { 0x57 } };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_basicinfo: ZC_GUILD_INFO (0x0b7b, PACKETVER >= 20200902: master char id and name).
pub fn clif_guild_basicinfo(c: &Ctx, sd: &Unit) {
    let Some(g) = guild::sd_guild(c, sd.id) else { return };
    let g = &g.guild;
    let p = PACKET_ZC_GUILD_INFO {
        PacketType: PACKET_ZC_GUILD_INFO::ID as i16,
        GDID: g.guild_id,
        level: g.guild_lv as i32,
        userNum: g.connect_member as i32,
        maxUserNum: g.max_member as i32,
        userAverageLevel: g.average_lv as i32,
        exp: g.exp.min(MAX_GUILD_EXP) as u32 as i32,
        maxExp: g.next_exp.min(MAX_GUILD_EXP) as u32 as i32,
        point: 0,  // Tax Points
        honor: 0,  // Honor: (left) Vulgar [-100,100] Famed (right)
        virtue: 0, // Virtue: (down) Wicked [-100,100] Righteous (up)
        emblemVersion: g.emblem_id,
        guildname: g.name.clone(),
        manageLand: crate::data::msg_txt(300 + guild::guild_checkcastles(c, g) as u16).to_string(),
        zeny: 0,
        masterGID: g.member[0].char_id as i32, // leader
        masterName: g.master.clone(),
    };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_allianceinfo: ZC_MYGUILD_BASIC_INFO (0x014c).
pub fn clif_guild_allianceinfo(c: &Ctx, sd: &Unit) {
    let Some(g) = guild::sd_guild(c, sd.id) else { return };
    let rg: Vec<RELATED_GUILD_INFO> = g
        .guild
        .alliance
        .iter()
        .filter(|a| a.guild_id > 0)
        .map(|a| RELATED_GUILD_INFO { relation: a.opposition, GDID: a.guild_id, guildname: a.name.clone() })
        .collect();
    let p = PACKET_ZC_MYGUILD_BASIC_INFO { PacketType: PACKET_ZC_MYGUILD_BASIC_INFO::ID as i16, PacketLength: 0, rgInfo: rg };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_memberlist: ZC_MEMBERMGR_INFO (0x0b7d, PACKETVER >= 20200902: last login and name).
pub fn clif_guild_memberlist(c: &Ctx, sd: &Unit) {
    let Some(g) = guild::sd_guild(c, sd.id) else { return };
    let g = &g.guild;
    let mut list = Vec::new();
    for i in 0..g.max() {
        let m = &g.member[i];
        if m.account_id == 0 {
            continue;
        }
        list.push(GUILD_MEMBER_INFO {
            AID: m.account_id,
            GID: m.char_id,
            head: m.hair,
            headPalette: m.hair_color,
            sex: m.gender,
            job: m.class,
            level: m.lv,
            contributionExp: m.exp.min(MAX_GUILD_EXP) as u32 as i32,
            currentState: m.online as i32,
            positionID: m.position as i32,
            lastLoginTime: m.last_login,
            char_name: m.name.clone(),
        });
    }
    let p = PACKET_ZC_MEMBERMGR_INFO { PacketType: PACKET_ZC_MEMBERMGR_INFO::ID as i16, packetLength: 0, guildMemberInfo: list };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_positionnamelist: ZC_POSITION_ID_NAME_INFO (0x0166, written by hand in rAthena).
fn clif_guild_positionnamelist(c: &Ctx, sd: &Unit) {
    let Some(g) = guild::sd_guild(c, sd.id) else { return };
    let mut w = Vec::with_capacity(MAX_GUILDPOSITION * 28 + 4);
    w.extend_from_slice(&0x0166u16.to_le_bytes());
    w.extend_from_slice(&((MAX_GUILDPOSITION * 28 + 4) as u16).to_le_bytes());
    for i in 0..MAX_GUILDPOSITION {
        w.extend_from_slice(&(i as u32).to_le_bytes());
        crate::net::wire::put_str(&mut w, &g.guild.position[i].name, NAME_LENGTH);
    }
    send_self(c, sd, &w);
}

/// clif.cpp clif_guild_positioninfolist: ZC_POSITION_INFO (0x0160).
fn clif_guild_positioninfolist(c: &Ctx, sd: &Unit) {
    let Some(g) = guild::sd_guild(c, sd.id) else { return };
    let info: Vec<PACKET_ZC_POSITION_INFO_sub> = (0..MAX_GUILDPOSITION)
        .map(|i| PACKET_ZC_POSITION_INFO_sub { positionID: i as i32, right: g.guild.position[i].mode, ranking: i as i32, payRate: g.guild.position[i].exp_mode })
        .collect();
    let p = PACKET_ZC_POSITION_INFO { PacketType: PACKET_ZC_POSITION_INFO::ID as i16, PacketLength: 0, posInfo: info };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_positionchanged: ZC_ACK_CHANGE_GUILD_POSITIONINFO (0x0174), one position, to the guild.
pub fn clif_guild_positionchanged(c: &Ctx, g: &MmoGuild, idx: usize) {
    let Some(sd) = guild::guild_getavailablesd(g).and_then(|a| unit::get(c, a)) else { return };
    let info = PACKET_ZC_ACK_CHANGE_GUILD_POSITIONINFO_sub { positionID: idx as i32, mode: g.position[idx].mode, ranking: idx as i32, payRate: g.position[idx].exp_mode, posName: g.position[idx].name.clone() };
    let p = PACKET_ZC_ACK_CHANGE_GUILD_POSITIONINFO { packetType: PACKET_ZC_ACK_CHANGE_GUILD_POSITIONINFO::ID as i16, packetLength: 0, posInfo: vec![info] };
    send(c, &p.to_vec(), &sd, GTarget::Guild);
}

/// clif.cpp clif_guild_memberpositionchanged: ZC_ACK_REQ_CHANGE_MEMBERS (0x0156), one member, to the guild.
pub fn clif_guild_memberpositionchanged(c: &Ctx, g: &MmoGuild, idx: usize) {
    let Some(sd) = guild::guild_getavailablesd(g).and_then(|a| unit::get(c, a)) else { return };
    let m = PACKET_ZC_ACK_REQ_CHANGE_MEMBERS_sub { accId: g.member[idx].account_id, charId: g.member[idx].char_id, positionID: g.member[idx].position as i32 };
    let p = PACKET_ZC_ACK_REQ_CHANGE_MEMBERS { packetType: PACKET_ZC_ACK_REQ_CHANGE_MEMBERS::ID as i16, packetLength: 0, members: vec![m] };
    send(c, &p.to_vec(), &sd, GTarget::Guild);
}

/// clif.cpp clif_guild_emblem: 0x0152 (written by hand: <len>.W <guild id>.L <emblem id>.L <data>) when the guild has
/// an emblem.
pub fn clif_guild_emblem(c: &Ctx, sd: &Unit, g: &MmoGuild) {
    if g.emblem_len <= 0 {
        return;
    }
    let len = g.emblem_len as usize;
    let mut w = Vec::with_capacity(len + 12);
    w.extend_from_slice(&0x0152u16.to_le_bytes());
    w.extend_from_slice(&((len + 12) as u16).to_le_bytes());
    w.extend_from_slice(&g.guild_id.to_le_bytes());
    w.extend_from_slice(&g.emblem_id.to_le_bytes());
    let mut data: Vec<u8> = g.emblem_data.iter().copied().take(len).collect();
    data.resize(len, 0);
    w.extend_from_slice(&data);
    send_self(c, sd, &w);
}

/// clif.cpp clif_guild_emblem_area: ZC_CHANGE_GUILD (0x0b47) with status_get_guild_id / status_get_emblem_id, to the
/// area (guild flags and guardians: woe.rs unit_guild_ids).
pub fn clif_guild_emblem_area(c: &Ctx, bl: &Unit) {
    let (guild_id, emblem_id) = crate::woe::unit_guild_ids(c, bl);
    let p = PACKET_ZC_CHANGE_GUILD { packetType: PACKET_ZC_CHANGE_GUILD::ID as i16, guild_id, emblem_id: emblem_id as u32, AID: bl.id };
    clif::send(c, &p.to_vec(), bl, Target::Area);
}

/// clif.cpp clif_guild_skillinfo: ZC_GUILD_SKILLINFO (0x0162), the skills whose requirements are met.
pub fn clif_guild_skillinfo(c: &Ctx, sd: &Unit) {
    let Some(g) = guild::sd_guild(c, sd.id) else { return };
    let g = &g.guild;
    let mut list = Vec::new();
    for i in 0..MAX_GUILDSKILL {
        let s = &g.skill[i];
        if s.id <= 0 {
            continue;
        }
        if !guild::guild_check_skill_require(g, s.id) {
            continue;
        }
        let id = s.id as u16;
        let lv = s.lv;
        let (sp, range2) = if lv > 0 { (crate::db::skills::skill_get_sp(id, lv as u16), crate::db::skills::skill_get_range(id, lv as u16)) } else { (0, 0) };
        let up = lv < crate::db::skills::guild_skill_get_max(id) as i32 && g.member[0].sd == sd.id && g.member[0].sd != 0;
        list.push(GUILD_SKILLDATA {
            id,
            inf: crate::db::skills::skill_get_inf(id),
            level: lv as u16,
            sp: sp as u16,
            range2: range2 as u16,
            name: crate::db::skills::skill_get_name(id).to_string(),
            upFlag: up as u8,
        });
    }
    let p = PACKET_ZC_GUILD_SKILLINFO { PacketType: PACKET_ZC_GUILD_SKILLINFO::ID as i16, PacketLength: 0, skillPoint: g.skill_point as i16, skillInfo: list };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_notice: ZC_GUILD_NOTICE (0x016f), unless both texts are empty.
pub fn clif_guild_notice(c: &Ctx, sd: &Unit) {
    let Some(g) = guild::sd_guild(c, sd.id) else { return };
    if g.guild.mes1.is_empty() && g.guild.mes2.is_empty() {
        return;
    }
    let p = PACKET_ZC_GUILD_NOTICE { packetType: PACKET_ZC_GUILD_NOTICE::ID as i16, subject: trunc(&g.guild.mes1, MAX_GUILDMES1), notice: trunc(&g.guild.mes2, MAX_GUILDMES2) };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_invite: ZC_REQ_JOIN_GUILD (0x016a) to the invited player.
pub fn clif_guild_invite(c: &Ctx, sd: &Unit, g: &MmoGuild) {
    let p = PACKET_ZC_REQ_JOIN_GUILD { packetType: PACKET_ZC_REQ_JOIN_GUILD::ID as i16, guild_id: g.guild_id as u32, guild_name: g.name.clone() };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_inviteack: ZC_ACK_REQ_JOIN_GUILD (0 already in a guild, 1 rejected, 2 accepted, 3 full).
pub fn clif_guild_inviteack(c: &Ctx, sd: &Unit, flag: i32) {
    let p = PACKET_ZC_ACK_REQ_JOIN_GUILD { packetType: PACKET_ZC_ACK_REQ_JOIN_GUILD::ID as i16, result: flag as u8 };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_leave: ZC_ACK_LEAVE_GUILD (guildLeave = 0x0a83: char id and reason) to the guild (GUILD_NOBG).
pub fn clif_guild_leave(c: &Ctx, sd: &Unit, _name: &str, char_id: u32, mes: &str) {
    let p = PACKET_ZC_ACK_LEAVE_GUILD2 { packetType: 0x0a83, GID: char_id, reason: trunc(mes, 40) };
    send(c, &p.to_vec(), sd, GTarget::GuildNobg);
}

/// clif.cpp clif_guild_expulsion: ZC_ACK_BAN_GUILD (guildExpulsion = 0x0a82: reason and char id) to the guild.
pub fn clif_guild_expulsion(c: &Ctx, sd: &Unit, _name: &str, char_id: u32, mes: &str) {
    let p = PACKET_ZC_ACK_BAN_GUILD3 { packetType: 0x0a82, reason: trunc(mes, 40), GID: char_id };
    send(c, &p.to_vec(), sd, GTarget::GuildNobg);
}

/// clif.cpp clif_guild_expulsionlist: ZC_BAN_LIST (0x0b7c: char id, reason, name).
fn clif_guild_expulsionlist(c: &Ctx, sd: &Unit) {
    let Some(g) = guild::sd_guild(c, sd.id) else { return };
    let chars: Vec<PACKET_ZC_BAN_LIST_sub> = g
        .guild
        .expulsion
        .iter()
        .filter(|e| e.account_id != 0)
        .map(|e| PACKET_ZC_BAN_LIST_sub { char_id: e.char_id as i32, message: trunc(&e.mes, 40), char_name: e.name.clone() })
        .collect();
    let p = PACKET_ZC_BAN_LIST { packetType: PACKET_ZC_BAN_LIST::ID as i16, packetLen: 0, chars };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_message: ZC_GUILD_CHAT (0x017f) to the guild through its first online member (GUILD_NOBG).
pub fn clif_guild_message(c: &Ctx, g: &MmoGuild, mes: &[u8]) {
    // -1 for null terminator
    let max_len = crate::net::CHAT_SIZE_MAX - PACKET_ZC_GUILD_CHAT::SIZE - 1;
    let Some(sd) = guild::guild_getavailablesd(g).and_then(|a| unit::get(c, a)) else { return };
    let mut len = mes.len();
    if len == 0 {
        return;
    } else if len > max_len {
        log::warn!("clif_guild_message: Truncated message (len={len}, max={max_len}, guild_id={}).", g.guild_id);
        len = max_len;
    }
    // safestrncpy(p->message, mes, len + 1)
    let mut m: Vec<u8> = mes.iter().copied().take_while(|&b| b != 0).take(len).collect();
    m.resize(len + 1, 0);
    let p = PACKET_ZC_GUILD_CHAT { packetType: PACKET_ZC_GUILD_CHAT::ID as i16, packetLength: 0, message: m };
    send(c, &p.to_vec(), &sd, GTarget::GuildNobg);
}

/// clif.cpp clif_guild_reqalliance: ZC_REQ_ALLY_GUILD (0x0171).
pub fn clif_guild_reqalliance(c: &Ctx, sd: &Unit, account_id: u32, name: &str) {
    let p = PACKET_ZC_REQ_ALLY_GUILD { packetType: PACKET_ZC_REQ_ALLY_GUILD::ID as i16, inviterId: account_id, inviterGuildName: trunc(name, NAME_LENGTH) };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_allianceack: ZC_ACK_REQ_ALLY_GUILD (0x0173).
pub fn clif_guild_allianceack(c: &Ctx, sd: &Unit, flag: u8) {
    let p = PACKET_ZC_ACK_REQ_ALLY_GUILD { packetType: PACKET_ZC_ACK_REQ_ALLY_GUILD::ID as i16, flag };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_delalliance: ZC_DELETE_RELATED_GUILD (0x0184).
pub fn clif_guild_delalliance(c: &Ctx, sd: &Unit, guild_id: u32, flag: u32) {
    let p = PACKET_ZC_DELETE_RELATED_GUILD { packetType: PACKET_ZC_DELETE_RELATED_GUILD::ID as i16, allyID: guild_id, flag };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_oppositionack: ZC_ACK_REQ_HOSTILE_GUILD (0x0181).
pub fn clif_guild_oppositionack(c: &Ctx, sd: &Unit, flag: u8) {
    let p = PACKET_ZC_ACK_REQ_HOSTILE_GUILD { packetType: PACKET_ZC_ACK_REQ_HOSTILE_GUILD::ID as i16, flag };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_broken: ZC_ACK_DISORGANIZE_GUILD_RESULT (0x015e).
pub fn clif_guild_broken(c: &Ctx, sd: &Unit, flag: i32) {
    let p = PACKET_ZC_ACK_DISORGANIZE_GUILD_RESULT { packetType: PACKET_ZC_ACK_DISORGANIZE_GUILD_RESULT::ID as i16, result: flag };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_guild_position_selected: ZC_GUILD_POSITION (0x0afd, PACKETVER >= 20180605) with the position name
/// (when the player has a position), to the area.
pub fn clif_guild_position_selected(c: &Ctx, sd: &Unit) {
    let mut position = Vec::new();
    if let Some(g) = guild::sd_guild(c, sd.id) {
        let ps = guild::guild_getposition(c, sd.id);
        if ps != -1 {
            if let Some(p) = g.guild.position.get(ps.max(0) as usize) {
                crate::net::wire::put_str(&mut position, &p.name, NAME_LENGTH);
            }
        }
    }
    let p = PACKET_ZC_GUILD_POSITION { packetType: PACKET_ZC_GUILD_POSITION::ID as i16, packetLength: 0, AID: sd.id, position };
    let mut w = p.to_vec();
    let len = w.len() as u16;
    w[2..4].copy_from_slice(&len.to_le_bytes());
    clif::send(c, &w, sd, Target::Area);
}

/// clif.cpp clif_skillup: ZC_SKILLINFO_UPDATE (the guild's answer to a guild skill level-up; skill_get_index 0 sends
/// nothing).
pub fn clif_skillup(c: &Ctx, sd: &Unit, skill_id: u16, lv: u16, range: i32, upgradable: bool) {
    if crate::db::skills::skill_get_index(skill_id) == 0 {
        return;
    }
    let p = PACKET_ZC_SKILLINFO_UPDATE {
        packetType: PACKET_ZC_SKILLINFO_UPDATE::ID,
        skillId: skill_id,
        level: lv,
        sp: crate::db::skills::skill_get_sp(skill_id, lv) as u16,
        range2: range as u16,
        upFlag: upgradable as u8,
    };
    send_self(c, sd, &p.to_vec());
}

// ------------------------------------------------------------------------------------------------ parsers

fn pos_of(pkt: &[u8], i: usize, default: u16) -> usize {
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    packet_pos(cmd).get(i).copied().unwrap_or(default) as usize
}

fn rd_u32(pkt: &[u8], off: usize) -> u32 {
    Reader::at(pkt, off).u32().unwrap_or(0)
}

fn rd_i32(pkt: &[u8], off: usize) -> i32 {
    Reader::at(pkt, off).i32().unwrap_or(0)
}

/// RFIFOCP(fd, off) read as a C string of at most `max` bytes (the packet's bytes from `off`).
fn cstr_at(pkt: &[u8], off: usize, max: usize) -> Vec<u8> {
    pkt.get(off..).unwrap_or(&[]).iter().copied().take_while(|&b| b != 0).take(max).collect()
}

/// clif.cpp clif_parse_* for the guild packets. Returns false when `func` is not one of them.
pub fn parse(c: &Ctx, aid: u32, func: &str, pkt: &[u8]) -> bool {
    match func {
        "clif_parse_CreateGuild" => parse_create_guild(c, aid, pkt),
        "clif_parse_GuildCheckMaster" => {
            if let Some(sd) = unit::get(c, aid) {
                clif_guild_masterormember(c, &sd);
            }
        }
        "clif_parse_GuildRequestInfo" => parse_guild_request_info(c, aid, pkt),
        "clif_parse_GuildChangePositionInfo" => parse_guild_change_position_info(c, aid, pkt),
        "clif_parse_GuildChangeMemberPosition" => parse_guild_change_member_position(c, aid, pkt),
        "clif_parse_GuildRequestEmblem" => parse_guild_request_emblem(c, aid, pkt),
        "clif_parse_GuildChangeEmblem" => parse_guild_change_emblem(c, aid, pkt),
        "clif_parse_GuildChangeEmblem2" => parse_guild_change_emblem2(c, aid, pkt),
        "clif_parse_GuildChangeNotice" => parse_guild_change_notice(c, aid, pkt),
        "clif_parse_GuildInvite" => {
            let t = rd_u32(pkt, 2);
            guild::guild_invite(c, aid, pc::get(c, t).map(|_| t));
        }
        "clif_parse_GuildInvite2" => {
            // safestrncpy(nick, p->name, NAME_LENGTH)
            let nick = from_latin1(&cstr_at(pkt, 2, NAME_LENGTH - 1));
            guild::guild_invite(c, aid, pc::nick2sd(c, &nick, false).map(|u| u.id));
        }
        "clif_parse_GuildReplyInvite" => {
            let Some(p) = PACKET_CZ_JOIN_GUILD::read(&mut Reader::new(pkt)) else { return true };
            guild::guild_reply_invite(c, aid, p.guild_id as i32, p.answer);
        }
        "clif_parse_GuildLeave" => {
            let Some(p) = PACKET_CZ_REQ_LEAVE_GUILD::read(&mut Reader::new(pkt)) else { return true };
            guild::guild_leave(c, aid, p.guild_id as i32, p.AID, p.CID, &from_latin1(&cstr_at(pkt, 14, 40)));
            let _ = p.message;
        }
        "clif_parse_GuildExpulsion" => {
            let Some(p) = PACKET_CZ_REQ_BAN_GUILD::read(&mut Reader::new(pkt)) else { return true };
            guild::guild_expulsion(c, aid, p.guild_id as i32, p.AID, p.CID, &from_latin1(&cstr_at(pkt, 14, 40)));
        }
        "clif_parse_GuildMessage" => {
            // validate packet and retrieve name and message
            let Some((_name, _message, output)) = crate::parse::process_message(c, aid, pkt, false) else { return true };
            if crate::battleground::bg_id_of(c, aid) != 0 {
                let out = latin1(&output);
                crate::battleground::bg_send_message(c, aid, &out, out.len());
            } else {
                guild::guild_send_message(c, aid, &latin1(&output));
            }
        }
        "clif_parse_GuildRequestAlliance" => parse_guild_request_alliance(c, aid, pkt),
        "clif_parse_GuildReplyAlliance" => {
            let acc = rd_u32(pkt, pos_of(pkt, 0, 2));
            let flag = rd_i32(pkt, pos_of(pkt, 1, 6));
            guild::guild_reply_reqalliance(c, aid, acc, flag);
        }
        "clif_parse_GuildDelAlliance" => parse_guild_del_alliance(c, aid, pkt),
        "clif_parse_GuildOpposition" => parse_guild_opposition(c, aid, pkt),
        "clif_parse_GuildBreak" => {
            // p->key (40 bytes) as a C string
            let key = from_latin1(&cstr_at(pkt, 2, 40));
            guild::guild_break(c, aid, &key);
        }
        "clif_parse_guild_castle_info_request" => parse_guild_castle_info_request(c, aid, pkt),
        "clif_parse_guild_castle_teleport_request" => parse_guild_castle_teleport_request(c, aid, pkt),
        _ => return false,
    }
    true
}

/// clif.cpp clif_parse_CreateGuild (CZ_REQ_MAKE_GUILD 0x0165: char id, name).
fn parse_create_guild(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    // name[NAME_LENGTH-1] = '\0'
    let name = from_latin1(&cstr_at(pkt, pos_of(pkt, 1, 6), NAME_LENGTH - 1));
    if crate::npc::mapflag(c, sd.map, guild::MF_GUILDLOCK) {
        // Guild locked.
        clif::displaymessage(c, &sd, crate::data::msg_txt(228));
        return;
    }
    // sd->clan (see guild::in_clan): the client shows its own "You are currently joined in Clan !!"
    if guild::in_clan(c, aid) {
        return;
    }
    guild::guild_create(c, aid, &name, guild::guild_emperium_check());
}

/// clif.cpp clif_parse_GuildRequestInfo (CZ_REQ_GUILD_MENU 0x014f: type 0 basic, 1 members, 2 positions, 3 skills,
/// 4 expulsions).
fn parse_guild_request_info(c: &Ctx, aid: u32, pkt: &[u8]) {
    let kind = rd_i32(pkt, pos_of(pkt, 0, 2));
    if guild::guild_id_of(c, aid) == 0 && crate::battleground::bg_id_of(c, aid) == 0 {
        return;
    }
    let Some(sd) = unit::get(c, aid) else { return };
    match kind {
        0 => {
            // Basic Information Guild, hostile alliance information
            clif_guild_basicinfo(c, &sd);
            clif_guild_allianceinfo(c, &sd);
            clif_guild_castle_list(c, &sd);
        }
        1 => {
            // Members list, list job title
            clif_guild_positionnamelist(c, &sd);
            clif_guild_memberlist(c, &sd);
        }
        2 => {
            // List job title, title information list
            clif_guild_positionnamelist(c, &sd);
            clif_guild_positioninfolist(c, &sd);
        }
        3 => clif_guild_skillinfo(c, &sd), // Skill list
        4 => clif_guild_expulsionlist(c, &sd), // Expulsion list
        _ => log::error!("clif: guild request info: unknown type {kind}"),
    }
}

/// clif.cpp clif_parse_GuildChangePositionInfo (CZ_REG_CHANGE_GUILD_POSITIONINFO 0x0161: entries of 40 bytes).
fn parse_guild_change_position_info(c: &Ctx, aid: u32, pkt: &[u8]) {
    let len = Reader::at(pkt, pos_of(pkt, 0, 2)).u16().unwrap_or(0) as usize;
    let idxgpos = pos_of(pkt, 1, 4);
    if !gx(c, aid).gmaster_flag {
        return;
    }
    let gid = guild::guild_id_of(c, aid);
    let mut i = idxgpos;
    while i < len {
        // RFIFOL(i), RFIFOL(i+4), RFIFOL(i+12), RFIFOCP(i+16)
        let name = from_latin1(&cstr_at(pkt, i + 16, NAME_LENGTH - 1));
        guild::guild_change_position(gid, rd_i32(pkt, i), rd_i32(pkt, i + 4), rd_i32(pkt, i + 12), &name);
        i += 40;
    }
}

/// clif.cpp clif_parse_GuildChangeMemberPosition (CZ_REQ_CHANGE_MEMBERPOS 0x0155).
fn parse_guild_change_member_position(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !gx(c, aid).gmaster_flag {
        return;
    }
    let Some(p) = PACKET_CZ_REQ_CHANGE_MEMBERPOS::read(&mut Reader::new(pkt)) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    let gid = guild::guild_id_of(c, aid);
    for entry in p.list.iter() {
        // Guild leadership change
        if entry.position == 0 {
            if !guild::guild_leaderchange_woe() && guild::is_agit_start(c) {
                crate::clif_items::msg(c, &sd, 2963); // MSI_IMPOSSIBLE_CHANGE_GUILD_MASTER_IN_SIEGE_TIME
                return;
            }
            let last = guild::sd_guild(c, aid).map(|g| g.guild.last_leader_change).unwrap_or(0);
            if guild::guild_leaderchange_delay() != 0 && c.tick() / 1000 - last < guild::guild_leaderchange_delay() {
                crate::clif_items::msg(c, &sd, 2964); // MSI_IMPOSSIBLE_CHANGE_GUILD_MASTER_NOT_TIME
                return;
            }
            guild::guild_gm_change(c, gid, entry.CID);
            // No further entries will be processed - the requesting player lost his guild master status
            return;
        } else if entry.position > 0 {
            guild::guild_change_memberposition(gid, entry.AID, entry.CID, entry.position as i16);
        }
    }
}

/// clif.cpp clif_parse_GuildRequestEmblem (CZ_REQ_GUILD_EMBLEM_IMG 0x0151).
fn parse_guild_request_emblem(c: &Ctx, aid: u32, pkt: &[u8]) {
    let guild_id = rd_i32(pkt, pos_of(pkt, 0, 2));
    let Some(sd) = unit::get(c, aid) else { return };
    if let Some(g) = guild::guild_search(c, guild_id) {
        clif_guild_emblem(c, &sd, &g.guild);
    }
}

/// clif.cpp e_result_validate_emblem.
const EMBVALIDATE_SUCCESS: i32 = 0;
const EMBVALIDATE_ERR_RAW_FILEFORMAT: i32 = 1;
const EMBVALIDATE_ERR_TRANSPARENCY: i32 = 2;

/// clif.cpp clif_validate_emblem: the data must inflate (zlib, into 1800 bytes) to a bitmap whose header is sane, and
/// hold at most emblem_transparency_limit % of magenta (transparent) pixels. The transparency scan reads 4 bytes at
/// every offset, up to 2 bytes past the inflated data (uninitialised stack bytes in rAthena; zero here).
fn clif_validate_emblem(emblem: &[u8]) -> i32 {
    let buf = match miniz_oxide::inflate::decompress_to_vec_zlib_with_limit(emblem, 1800) {
        Ok(b) => b,
        Err(_) => return EMBVALIDATE_ERR_RAW_FILEFORMAT,
    };
    let buf_len = buf.len();
    let rw = |o: usize| -> u32 { u16::from_le_bytes([*buf.get(o).unwrap_or(&0), *buf.get(o + 1).unwrap_or(&0)]) as u32 };
    let rl = |o: usize| -> u32 { u32::from_le_bytes([*buf.get(o).unwrap_or(&0), *buf.get(o + 1).unwrap_or(&0), *buf.get(o + 2).unwrap_or(&0), *buf.get(o + 3).unwrap_or(&0)]) };
    if !(buf_len >= 18 && rw(0) == 0x4d42 && rl(2) as usize == buf_len) {
        return EMBVALIDATE_ERR_RAW_FILEFORMAT;
    }
    let offset = rl(10) as usize;
    if offset >= buf_len {
        return EMBVALIDATE_ERR_RAW_FILEFORMAT;
    }
    if guild::EMBLEM_TRANSPARENCY_LIMIT != 100 {
        let mut transcount: u64 = 1;
        let mut tmp = [0u32; 3];
        for i in offset..buf_len - 1 {
            let j = i % 3;
            tmp[j] = rl(i);
            if j == 2 && tmp[0] == 0xFFFF00FF && tmp[1] == 0xFFFF00 && tmp[2] == 0xFF00FFFF {
                transcount += 1; // if pixel is transparent
            }
        }
        if (transcount * 300) / (buf_len - offset) as u64 > guild::EMBLEM_TRANSPARENCY_LIMIT as u64 {
            return EMBVALIDATE_ERR_TRANSPARENCY;
        }
    }
    EMBVALIDATE_SUCCESS
}

/// clif.cpp clif_parse_GuildChangeEmblem (CZ_REGISTER_GUILD_EMBLEM_IMG 0x0153).
fn parse_guild_change_emblem(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    let len_word = Reader::at(pkt, pos_of(pkt, 0, 2)).u16().unwrap_or(0) as usize;
    let emblem_len = len_word.wrapping_sub(4);
    let off = pos_of(pkt, 1, 4);
    if emblem_len == 0 || !gx(c, aid).gmaster_flag {
        return;
    }
    let emblem: Vec<u8> = pkt.get(off..).unwrap_or(&[]).iter().copied().take(emblem_len).collect();
    if !guild::EMBLEM_WOE_CHANGE && guild::is_agit_start(c) {
        clif::messagecolor_target(c, &sd, crate::social::COLOR_RED, &latin1(crate::data::msg_txt(385)), false, Target::SelfOnly, None); // You not allowed to change emblem during woe
        return;
    }
    let emb_val = clif_validate_emblem(&emblem);
    if emb_val == EMBVALIDATE_ERR_RAW_FILEFORMAT {
        log::warn!("clif_parse_GuildChangeEmblem: Rejected malformed guild emblem (size={emblem_len}, accound_id={aid}, guild_id={}).", guild::guild_id_of(c, aid));
        clif::messagecolor_target(c, &sd, crate::social::COLOR_RED, &latin1(crate::data::msg_txt(386)), false, Target::SelfOnly, None); // The chosen emblem was detected invalid
        return;
    } else if emb_val == EMBVALIDATE_ERR_TRANSPARENCY {
        let out: Vec<u8> = latin1(&crate::data::format_msg(crate::data::msg_txt(387), &[&guild::EMBLEM_TRANSPARENCY_LIMIT])).into_iter().take(127).collect();
        clif::messagecolor_target(c, &sd, crate::social::COLOR_RED, &out, false, Target::SelfOnly, None);
        return;
    }
    guild::guild_change_emblem(c, aid, &emblem);
}

/// clif.cpp clif_parse_GuildChangeEmblem2 (CZ_REQ_ADD_NEW_EMBLEM 0x0b46: guild id, version).
fn parse_guild_change_emblem2(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_ADD_NEW_EMBLEM::read(&mut Reader::new(pkt)) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(g) = guild::sd_guild(c, aid) else { return };
    if g.guild.guild_id != p.guild_id as i32 {
        return;
    }
    if !gx(c, aid).gmaster_flag {
        return;
    }
    if !guild::EMBLEM_WOE_CHANGE && guild::is_agit_start(c) {
        clif::messagecolor_target(c, &sd, crate::social::COLOR_RED, &latin1(crate::data::msg_txt(385)), false, Target::SelfOnly, None);
        return;
    }
    guild::guild_change_emblem_version(c, aid, p.version as i32);
}

/// clif.cpp clif_parse_GuildChangeNotice (CZ_GUILD_NOTICE 0x016e: guild id, 60-byte subject, 120-byte notice), with
/// the multilanguage marker fixes.
fn parse_guild_change_notice(c: &Ctx, aid: u32, pkt: &[u8]) {
    let guild_id = rd_i32(pkt, pos_of(pkt, 0, 2));
    let mut o1 = pos_of(pkt, 1, 6);
    let mut o2 = pos_of(pkt, 2, 66);
    if !gx(c, aid).gmaster_flag {
        return;
    }
    let b = |o: usize| pkt.get(o).copied().unwrap_or(0);
    // compensate for some client defects when using multilanguage mode
    if b(o1) == b'|' && b(o1 + 3) == b'|' {
        o1 += 3; // skip duplicate marker
    }
    if b(o2) == b'|' && b(o2 + 3) == b'|' {
        o2 += 3; // skip duplicate marker
    }
    let mes1: Vec<u8> = pkt.get(o1..).unwrap_or(&[]).to_vec();
    let mut mes2: Vec<u8> = pkt.get(o2..).unwrap_or(&[]).to_vec();
    if b(o2) == b'|' {
        // delete extra space at the end of string: msg2[strnlen(msg2, MAX_GUILDMES2) - 1] = '\0'
        let n = mes2.iter().take(MAX_GUILDMES2).take_while(|&&x| x != 0).count();
        if n > 0 {
            mes2[n - 1] = 0;
        }
    }
    guild::guild_change_notice(c, aid, guild_id, &mes1, &mes2);
}

/// clif.cpp clif_parse_GuildRequestAlliance (CZ_REQ_ALLY_GUILD 0x0170).
fn parse_guild_request_alliance(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !gx(c, aid).gmaster_flag {
        return;
    }
    let Some(sd) = unit::get(c, aid) else { return };
    if crate::npc::mapflag(c, sd.map, guild::MF_GUILDLOCK) {
        // Guild locked.
        clif::displaymessage(c, &sd, crate::data::msg_txt(228));
        return;
    }
    let t = rd_u32(pkt, pos_of(pkt, 0, 2));
    let t_sd = pc::get(c, t).map(|_| t);
    // @noask [LuzZza]
    if let Some(tu) = t_sd.and_then(|t| unit::get(c, t)) {
        if crate::social::sx(c, tu.id).noask {
            crate::social::clif_noask_sub(c, &sd, &tu, 396); // Autorejected alliance request from %s.
            return;
        }
    }
    guild::guild_reqalliance(c, aid, t_sd);
}

/// clif.cpp clif_parse_GuildDelAlliance (CZ_REQ_DELETE_RELATED_GUILD 0x0183: guild id, relation).
fn parse_guild_del_alliance(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !gx(c, aid).gmaster_flag {
        return;
    }
    let Some(sd) = unit::get(c, aid) else { return };
    if crate::npc::mapflag(c, sd.map, guild::MF_GUILDLOCK) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(228));
        return;
    }
    guild::guild_delalliance(c, aid, rd_i32(pkt, pos_of(pkt, 0, 2)), rd_i32(pkt, pos_of(pkt, 1, 6)));
}

/// clif.cpp clif_parse_GuildOpposition (CZ_REQ_HOSTILE_GUILD 0x0180).
fn parse_guild_opposition(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !gx(c, aid).gmaster_flag {
        return;
    }
    let Some(sd) = unit::get(c, aid) else { return };
    if crate::npc::mapflag(c, sd.map, guild::MF_GUILDLOCK) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(228));
        return;
    }
    let t = rd_u32(pkt, pos_of(pkt, 0, 2));
    let t_sd = pc::get(c, t).map(|_| t);
    // @noask [LuzZza]
    if let Some(tu) = t_sd.and_then(|t| unit::get(c, t)) {
        if crate::social::sx(c, tu.id).noask {
            crate::social::clif_noask_sub(c, &sd, &tu, 397); // Autorejected opposition request from %s.
            return;
        }
    }
    guild::guild_opposition(c, aid, t_sd);
}

/// clif.cpp clif_parse_guild_castle_info_request (CZ_REQ_AGIT_INVESTMENT 0x0b2c): woe/clif.rs.
fn parse_guild_castle_info_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    crate::woe::clif::clif_parse_guild_castle_info_request(c, aid, pkt);
}

/// clif.cpp clif_parse_guild_castle_teleport_request (CZ_REQ_MOVE_GUILD_AGIT 0x0b28): woe/clif.rs.
fn parse_guild_castle_teleport_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    crate::woe::clif::clif_parse_guild_castle_teleport_request(c, aid, pkt);
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A 1-pixel 24-bit BMP, zlib-compressed: accepted; garbage and a wrong size field are refused.
    #[test]
    fn validate_emblem() {
        let mut bmp = vec![0u8; 58];
        bmp[0] = b'B';
        bmp[1] = b'M';
        bmp[2..6].copy_from_slice(&58u32.to_le_bytes());
        bmp[10..14].copy_from_slice(&54u32.to_le_bytes());
        let z = miniz_oxide::deflate::compress_to_vec_zlib(&bmp, 6);
        assert_eq!(clif_validate_emblem(&z), EMBVALIDATE_SUCCESS);
        assert_eq!(clif_validate_emblem(&[1, 2, 3]), EMBVALIDATE_ERR_RAW_FILEFORMAT);
        let mut bad = bmp.clone();
        bad[2..6].copy_from_slice(&57u32.to_le_bytes());
        assert_eq!(clif_validate_emblem(&miniz_oxide::deflate::compress_to_vec_zlib(&bad, 6)), EMBVALIDATE_ERR_RAW_FILEFORMAT);
        // all-magenta pixels: 1 + n transparent per 300 / bytes > 80
        let mut t = vec![0u8; 54 + 30];
        t[0] = b'B';
        t[1] = b'M';
        t[2..6].copy_from_slice(&(84u32).to_le_bytes());
        t[10..14].copy_from_slice(&54u32.to_le_bytes());
        for k in 0..10 {
            t[54 + k * 3] = 0xFF;
            t[55 + k * 3] = 0x00;
            t[56 + k * 3] = 0xFF;
        }
        assert_eq!(clif_validate_emblem(&miniz_oxide::deflate::compress_to_vec_zlib(&t, 6)), EMBVALIDATE_ERR_TRANSPARENCY);
    }
}
