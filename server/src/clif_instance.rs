//! clif.cpp's instance packets: clif_instance_create (ZC_MEMORIALDUNGEON_SUBSCRIPTION_INFO 0x02cb),
//! clif_instance_changewait (ZC_MEMORIALDUNGEON_SUBSCRIPTION_NOTIFY 0x02cc), clif_instance_status
//! (ZC_MEMORIALDUNGEON_INFO 0x02cd), clif_instance_changestatus (ZC_MEMORIALDUNGEON_NOTIFY 0x02ce), the destroy
//! button (clif_parse_MemorialDungeonCommand, CZ_MEMORIALDUNGEON_COMMAND 0x02cf), clif_instance_info, and the
//! client's load confirmation that carries it (clif_parse_blocking_playcancel / clif_loadConfirm: CZ_BLOCKING_PLAY_CANCEL
//! 0x0447 → ZC_NOTIFY_ACTORINIT 0x0b1b). rAthena writes the 0x02cb-0x02ce packets with WBUF* (no packet struct).

use crate::instance::{self, Recipient};
use crate::unit::Unit;
use crate::world::Ctx;

/// instance.hpp INSTANCE_NAME_LENGTH (60 + 1).
pub const INSTANCE_NAME_LENGTH: usize = 61;
/// clif.hpp COMMAND_MEMORIALDUNGEON_DESTROY_FORCE.
pub const COMMAND_MEMORIALDUNGEON_DESTROY_FORCE: i32 = 0x3;

/// safestrncpy(WBUFCP(buf, 2), name, INSTANCE_NAME_LENGTH): the name cut to 60 bytes, NUL-padded.
fn name_field(name: &str) -> [u8; INSTANCE_NAME_LENGTH] {
    let mut f = [0u8; INSTANCE_NAME_LENGTH];
    let b = crate::net::wire::latin1(name);
    let n = b.len().min(INSTANCE_NAME_LENGTH - 1);
    f[..n].copy_from_slice(&b[..n]);
    f
}

/// clif_send(buf, len, sd, target) with instance_getsd's player and target.
fn send(c: &Ctx, buf: &[u8], to: &Recipient) {
    let aid = match to {
        Recipient::One(a) | Recipient::Party(a) | Recipient::Guild(a) | Recipient::Clan(a) => *a,
    };
    let Some(sd) = crate::unit::get(c, aid) else { return };
    match to {
        Recipient::One(_) => crate::clif::send(c, buf, &sd, crate::clif::Target::SelfOnly),
        Recipient::Party(_) => crate::party::send(c, buf, &sd, crate::party::PTarget::Party),
        Recipient::Guild(_) => crate::clif_guild::send(c, buf, &sd, crate::clif_guild::GTarget::Guild),
        Recipient::Clan(_) => crate::clif::send(c, buf, &sd, crate::clif::Target::Clan),
    }
}

/// The database name of an instance (instance_db.find(umap_find(instances, id)->id)).
fn db_name(c: &Ctx, instance_id: i32) -> Option<String> {
    let idata = instance::find(c, instance_id)?;
    crate::db::misc::instance::instance_db().find(idata.id).map(|d| d.name.clone())
}

/// clif.cpp clif_instance_create: "the instancing information window" with the standby position (0x02cb).
pub fn clif_instance_create(c: &Ctx, instance_id: i32, num: usize) {
    let Some(to) = instance::instance_getsd(c, instance_id) else { return };
    let Some(name) = db_name(c, instance_id) else { return };
    let mut w = Vec::with_capacity(65);
    w.extend_from_slice(&0x02cbu16.to_le_bytes());
    w.extend_from_slice(&name_field(&name));
    w.extend_from_slice(&(num as i16).to_le_bytes());
    send(c, &w, &to);
}

/// clif.cpp clif_instance_changewait: the new standby position (0x02cc; 0xffff = none).
pub fn clif_instance_changewait(c: &Ctx, instance_id: i32, num: i32) {
    let Some(to) = instance::instance_getsd(c, instance_id) else { return };
    let mut w = Vec::with_capacity(4);
    w.extend_from_slice(&0x02ccu16.to_le_bytes());
    w.extend_from_slice(&(num as u16).to_le_bytes());
    send(c, &w, &to);
}

/// clif.cpp clif_instance_status: the instance's name and its two limits (0x02cd: keep_limit, idle_limit as unix
/// seconds, 0 = none).
pub fn clif_instance_status(c: &Ctx, instance_id: i32, limit1: u32, limit2: u32) {
    let Some(to) = instance::instance_getsd(c, instance_id) else { return };
    let Some(name) = db_name(c, instance_id) else { return };
    let mut w = Vec::with_capacity(71);
    w.extend_from_slice(&0x02cdu16.to_le_bytes());
    w.extend_from_slice(&name_field(&name));
    w.extend_from_slice(&limit1.to_le_bytes());
    w.extend_from_slice(&limit2.to_le_bytes());
    send(c, &w, &to);
}

/// clif.cpp clif_instance_changestatus: 0 notification, 1 life time over, 2 entry time over, 3 destroyed on request,
/// 4 creation failed (0x02ce).
pub fn clif_instance_changestatus(c: &Ctx, instance_id: i32, kind: u32, limit: u32) {
    let Some(to) = instance::instance_getsd(c, instance_id) else { return };
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x02ceu16.to_le_bytes());
    w.extend_from_slice(&kind.to_le_bytes());
    w.extend_from_slice(&limit.to_le_bytes());
    send(c, &w, &to);
}

/// clif.cpp clif_parse_MemorialDungeonCommand (0x02cf): the destroy button (not while trading, dead or inside an
/// instance).
pub fn parse_memorial_dungeon_command(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = crate::pc::get(c, aid) else { return };
    let Some(u) = crate::unit::get(c, aid) else { return };
    if crate::inventory::pc_istrading(&p) || crate::pc::isdead(c, aid) || crate::instance_map::map_instance_id(c, u.map) > 0 {
        return;
    }
    let mut r = crate::net::wire::Reader::new(pkt);
    let Some(p) = crate::net::packets::PACKET_CZ_MEMORIALDUNGEON_COMMAND::read(&mut r) else { return };
    if p.command == COMMAND_MEMORIALDUNGEON_DESTROY_FORCE {
        instance::instance_destroy_command(c, aid);
    }
}

/// clif.cpp clif_instance_info: the player's instances (its own, its party's, guild's, clan's) in the instance window.
pub fn clif_instance_info(c: &Ctx, aid: u32) {
    let iid = instance::sd_instance_id(c, aid);
    if iid > 0 {
        instance::instance_reqinfo(c, aid, iid);
    }
    let party_id = crate::party::party_id_of(c, aid);
    if party_id > 0 {
        if let Some(p) = crate::party::party_search(c, party_id) {
            if p.instance_id > 0 {
                instance::instance_reqinfo(c, aid, p.instance_id);
            }
        }
    }
    if let Some(g) = crate::guild::sd_guild(c, aid) {
        if g.instance_id > 0 {
            instance::instance_reqinfo(c, aid, g.instance_id);
        }
    }
    if let Some(clan_id) = crate::clan::sd_clan(c, aid) {
        let ciid = crate::clan::instance_id(c, clan_id);
        if ciid > 0 {
            instance::instance_reqinfo(c, aid, ciid as i32);
        }
    }
}

/// clif.cpp clif_loadConfirm (PACKETVER_RE_NUM >= 20190320): ZC_NOTIFY_ACTORINIT, then clif_instance_info.
pub fn clif_load_confirm(c: &Ctx, sd: &Unit) {
    let p = crate::net::packets::PACKET_ZC_NOTIFY_ACTORINIT { packetType: crate::net::packets::PACKET_ZC_NOTIFY_ACTORINIT::ID as i16 };
    crate::clif::send(c, &p.to_vec(), sd, crate::clif::Target::SelfOnly);
    clif_instance_info(c, sd.id);
}

/// clif.cpp clif_parse_blocking_playcancel (CZ_BLOCKING_PLAY_CANCEL 0x0447): the client closed its loading screen:
/// clif_loadConfirm, then the map's special popup (MF_SPECIALPOPUP; clif_specialpopup needs PACKETVER 20221005, so
/// nothing more is sent for this build).
pub fn parse_blocking_playcancel(c: &Ctx, aid: u32) {
    let Some(sd) = crate::unit::get(c, aid) else { return };
    clif_load_confirm(c, &sd);
    // int32 mf = map_getmapflag(sd->m, MF_SPECIALPOPUP); if (mf > 0) clif_specialpopup(*sd, mf): no packet before
    // PACKETVER 20221005 (script2/effects.rs specialpopup)
}
