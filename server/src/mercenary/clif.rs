//! The mercenary packets of clif.cpp: clif_mercenary_updatestatus (ZC_MER_PAR_CHANGE), clif_mercenary_info
//! (ZC_MER_INIT), clif_mercenary_skillblock (ZC_MER_SKILLINFO_LIST), and the requests clif_parse_mercenary_action
//! (CZ_MER_COMMAND) and the mercenary arms of clif_parse_HomMoveToMaster (CZ_REQUEST_MOVETOOWNER), clif_parse_HomMoveTo
//! (CZ_REQUEST_MOVENPC) and clif_parse_HomAttack (CZ_REQUEST_ACTNPC), which rAthena shares with the homunculi.

use super::data::{self, MercenaryData};
use crate::clif::{self, Target};
use crate::net::packets::packet_pos;
use crate::status::consts::*;
use crate::unit;
use crate::world::Ctx;

/// clif.cpp clif_session_isValid + clif_send(…, sd, SELF): to the player `aid` when online.
fn send_self(c: &Ctx, aid: u32, buf: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, buf, &sd, Target::SelfOnly);
    }
}

/// `rnd()%(atk2 - atk + 1) + batk + atk` capped to INT16_MAX: the ATK a mercenary shows (a random value between ATK and
/// ATK2). The modulo is computed in int (atk2 < atk gives a negative or zero divisor as in C: a zero divisor would
/// crash the C server; it is 1 here).
fn shown_atk(c: &Ctx, st: &crate::status::data::StatusData) -> i32 {
    let span = st.rhw.atk2 as i32 - st.rhw.atk as i32 + 1;
    let r = c.rnd() as i32;
    let roll = if span == 0 { 0 } else { r % span };
    (roll + st.batk + st.rhw.atk as i32).clamp(0, i16::MAX as i32)
}

/// clif.cpp clif_mercenary_updatestatus: ZC_MER_PAR_CHANGE (0x2a2) type.W value.L to the master.
pub fn clif_mercenary_updatestatus(c: &Ctx, aid: u32, type_: i32) {
    let Some(md) = data::of_master(c, aid) else { return };
    if unit::get(c, aid).is_none() {
        return;
    }
    let status = &md.battle_status;
    let value: i32 = match type_ {
        SP_ATK1 => shown_atk(c, status),
        SP_MATK1 => (status.matk_max as i32).min(u16::MAX as i32),
        SP_HIT => status.hit as i32,
        SP_CRITICAL => status.cri as i32 / 10,
        SP_DEF1 => status.def as i32,
        SP_MDEF1 => status.mdef as i32,
        SP_MERCFLEE => status.flee as i32,
        SP_ASPD => status.amotion as i32,
        SP_HP => status.hp as i32,
        SP_MAXHP => status.max_hp as i32,
        SP_SP => status.sp as i32,
        SP_MAXSP => status.max_sp as i32,
        SP_MERCKILLS => md.mercenary.kill_count as i32,
        SP_MERCFAITH => super::mercenary_get_faith(c, &md),
        // (WFIFOL(fd,4) is not written for other types: the send buffer's old bytes; none is ever asked for)
        _ => 0,
    };
    let mut w = Vec::with_capacity(8);
    w.extend_from_slice(&0x02a2u16.to_le_bytes());
    w.extend_from_slice(&(type_ as u16).to_le_bytes());
    w.extend_from_slice(&value.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_mercenary_info: ZC_MER_INIT (0x29b), the mercenary's base status data (80 bytes).
pub fn clif_mercenary_info(c: &Ctx, aid: u32) {
    let Some(md) = data::of_master(c, aid) else { return };
    if unit::get(c, aid).is_none() {
        return;
    }
    let status = &md.battle_status;
    let db = data::db_of(&md);
    let mut w = Vec::with_capacity(80);
    w.extend_from_slice(&0x029bu16.to_le_bytes());
    w.extend_from_slice(&md.id.to_le_bytes());
    // Mercenary shows ATK as a random value between ATK ~ ATK2
    let atk = shown_atk(c, status);
    w.extend_from_slice(&(atk as u16).to_le_bytes());
    w.extend_from_slice(&((status.matk_max as u32).min(u16::MAX as u32) as u16).to_le_bytes());
    w.extend_from_slice(&(status.hit as u16).to_le_bytes());
    w.extend_from_slice(&((status.cri / 10) as u16).to_le_bytes());
    w.extend_from_slice(&(status.def as u16).to_le_bytes());
    w.extend_from_slice(&(status.mdef as u16).to_le_bytes());
    w.extend_from_slice(&(status.flee as u16).to_le_bytes());
    w.extend_from_slice(&status.amotion.to_le_bytes());
    // safestrncpy(WFIFOCP(fd,22), md->db->name.c_str(), NAME_LENGTH)
    crate::net::wire::put_str(&mut w, db.map(|d| d.name.as_str()).unwrap_or(""), 24);
    w.extend_from_slice(&db.map(|d| d.lv).unwrap_or(0).to_le_bytes());
    w.extend_from_slice(&status.hp.to_le_bytes());
    w.extend_from_slice(&status.max_hp.to_le_bytes());
    w.extend_from_slice(&status.sp.to_le_bytes());
    w.extend_from_slice(&status.max_sp.to_le_bytes());
    // client_tick(time(nullptr) + (mercenary_get_lifetime(md) / 1000)): the expiry as unix seconds
    let now_s = (c.rc.timestamp.to_micros_since_unix_epoch() / 1_000_000) as i64;
    let expire = (now_s + super::mercenary_get_lifetime(c, &md) / 1000) as u32;
    w.extend_from_slice(&expire.to_le_bytes());
    w.extend_from_slice(&(super::mercenary_get_faith(c, &md) as u16).to_le_bytes());
    w.extend_from_slice(&super::mercenary_get_calls(c, &md).to_le_bytes());
    w.extend_from_slice(&md.mercenary.kill_count.to_le_bytes());
    w.extend_from_slice(&md.battle_status.rhw.range.to_le_bytes());
    send_self(c, aid, &w);
}

/// clif.cpp clif_mercenary_skillblock: ZC_MER_SKILLINFO_LIST (0x29d), the mercenary's skills in md->db->skill's
/// (unordered_map) order: id, inf, level, SP, range (skill_get_range2), name, upgradable 0.
pub fn clif_mercenary_skillblock(c: &Ctx, aid: u32) {
    let Some(md) = data::of_master(c, aid) else { return };
    let Some(db) = data::db_of(&md) else { return };
    if unit::get(c, aid).is_none() {
        return;
    }
    let mu = unit::get(c, md.id);
    let mut w = Vec::with_capacity(4 + 37 * crate::int_mercenary::MAX_MERCSKILL as usize);
    w.extend_from_slice(&0x029du16.to_le_bytes());
    w.extend_from_slice(&0u16.to_le_bytes());
    for &(id, lv) in &db.skill {
        if !(id >= crate::int_mercenary::MC_SKILLBASE && id < crate::int_mercenary::MC_SKILLBASE + crate::int_mercenary::MAX_MERCSKILL) {
            continue;
        }
        w.extend_from_slice(&id.to_le_bytes());
        w.extend_from_slice(&(crate::db::skills::skill_get_inf(id) as u32).to_le_bytes());
        w.extend_from_slice(&lv.to_le_bytes());
        w.extend_from_slice(&(crate::db::skills::skill_get_sp(id, lv) as u16).to_le_bytes());
        let range = mu.as_ref().map(|u| crate::skill::misc::skill_get_range2(c, u, id, lv, false)).unwrap_or(0);
        w.extend_from_slice(&(range as u16).to_le_bytes());
        crate::net::wire::put_str(&mut w, crate::db::skills::skill_get_name(id), 24);
        w.push(0); // Skillable for Mercenary?
    }
    let len = w.len() as u16;
    w[2..4].copy_from_slice(&len.to_le_bytes());
    send_self(c, aid, &w);
}

// ---------------------------------------------------------------------------------------------- requests

fn field_u8(pkt: &[u8], cmd: u16, n: usize) -> Option<u8> {
    let o = *packet_pos(cmd).get(n)? as usize;
    pkt.get(o).copied()
}
fn field_u32(pkt: &[u8], cmd: u16, n: usize) -> Option<u32> {
    let o = *packet_pos(cmd).get(n)? as usize;
    Some(u32::from_le_bytes([*pkt.get(o)?, *pkt.get(o + 1)?, *pkt.get(o + 2)?, *pkt.get(o + 3)?]))
}

/// The request handlers (net/mod.rs dispatches here by clif_parse_* name); false when `func` is not one of them.
pub fn parse(c: &Ctx, aid: u32, func: &str, pkt: &[u8]) -> bool {
    if pkt.len() < 2 {
        return false;
    }
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    match func {
        // clif_parse_mercenary_action (CZ_MER_COMMAND): 1 information, 2 delete
        "clif_parse_mercenary_action" => {
            let Some(option) = field_u8(pkt, cmd, 0) else { return true };
            let Some(md) = data::of_master(c, aid) else { return true };
            if option == 2 {
                super::mercenary_delete(c, md.id, 2);
            }
        }
        // clif_parse_HomMoveToMaster (CZ_REQUEST_MOVETOOWNER): the mercenary (or the homunculus) walks back to the master
        // (the one handler of the three shared names: homunculus/clif.rs leaves them here)
        "clif_parse_HomMoveToMaster" => {
            let Some(id) = field_u32(pkt, cmd, 0) else { return true };
            clif_parse_hom_move_to_master(c, aid, id);
        }
        // clif_parse_HomMoveTo (CZ_REQUEST_MOVENPC)
        "clif_parse_HomMoveTo" => {
            let Some(p) = crate::net::packets::PACKET_CZ_REQUEST_MOVENPC::read(&mut crate::net::wire::Reader::new(pkt)) else { return true };
            let Some((x, y, _)) = crate::net::wire::read_pos3(&p.PosDir) else { return true };
            clif_parse_hom_move_to(c, aid, p.GID, x, y);
        }
        // clif_parse_HomAttack (CZ_REQUEST_ACTNPC)
        "clif_parse_HomAttack" => {
            let (Some(id), Some(target_id), Some(action_type)) = (field_u32(pkt, cmd, 0), field_u32(pkt, cmd, 1), field_u8(pkt, cmd, 2)) else { return true };
            clif_parse_hom_attack(c, aid, id, target_id, action_type as i8);
        }
        _ => return false,
    }
    true
}

/// The unit of `id` when it is the player's mercenary (`sd->md && sd->md->id == id`).
fn own_mercenary(c: &Ctx, aid: u32, id: u32) -> Option<MercenaryData> {
    data::of_master(c, aid).filter(|md| md.id == id)
}

/// clif.cpp clif_parse_HomMoveToMaster (clif.cpp:15699-15714): `sd->md` first, then an active homunculus.
pub fn clif_parse_hom_move_to_master(c: &Ctx, aid: u32, id: u32) {
    let bl = if own_mercenary(c, aid, id).is_some() {
        id
    } else if let Some(hd) = crate::homunculus::clif::active_hd(c, aid, id) {
        hd.id // Moving Homunculus
    } else {
        return;
    };
    let Some(sd) = unit::get(c, aid) else { return };
    crate::pet::ai::unit_calc_pos(c, bl, sd.x as i32, sd.y as i32, sd.ud.dir);
    let Some(u) = unit::get(c, bl) else { return };
    unit::walktoxy(c, bl, u.ud.to_x, u.ud.to_y, 4);
}

/// clif.cpp clif_parse_HomMoveTo (clif.cpp:15719-15739): the mercenary (first) or the active homunculus walks to
/// (x, y).
pub fn clif_parse_hom_move_to(c: &Ctx, aid: u32, gid: u32, x: i16, y: i16) {
    let bl = if own_mercenary(c, aid, gid).is_some() {
        gid // Moving Mercenary
    } else if let Some(hd) = crate::homunculus::clif::active_hd(c, aid, gid) {
        hd.id // Moving Homunculus
    } else {
        return;
    };
    let Some(u) = unit::get(c, bl) else { return };
    if x < 0 || y < 0 || (x == u.x && y == u.y) {
        return;
    }
    unit::walktoxy(c, bl, x, y, 4);
}

/// clif.cpp clif_parse_HomAttack (clif.cpp:15746-15761): the active homunculus (first) or the mercenary attacks
/// `target_id`.
pub fn clif_parse_hom_attack(c: &Ctx, aid: u32, id: u32, target_id: u32, action_type: i8) {
    let bl = if let Some(hd) = crate::homunculus::clif::active_hd(c, aid, id) {
        hd.id
    } else if own_mercenary(c, aid, id).is_some() {
        id
    } else {
        return;
    };
    crate::battle::unit_attack::unit_attack(c, bl, target_id, (action_type != 0) as i32);
}
