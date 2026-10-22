//! The elemental packets of clif.cpp: clif_elemental_updatestatus (ZC_EL_PAR_CHANGE) and clif_elemental_info
//! (ZC_EL_INIT), and the HP meter the master gets for it (clif_hpmeter_single, status/clif.rs).

use super::data;
use crate::clif::{self, Target};
use crate::net::packets::PACKET_ZC_EL_PAR_CHANGE;
use crate::status::consts::*;
use crate::unit;
use crate::world::Ctx;

/// clif.cpp clif_elemental_updatestatus (PACKETVER >= 20100309): ZC_EL_PAR_CHANGE (0x81e) type.W value.L to the master
/// (only while sd.ed is set). Other types than HP / SP / max HP / max SP send 0.
pub fn clif_elemental_updatestatus(c: &Ctx, aid: u32, type_: i32) {
    let Some(ed) = data::of_master(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    let status = &ed.battle_status;
    let value = match type_ {
        SP_HP => status.hp,
        SP_MAXHP => status.max_hp,
        SP_SP => status.sp,
        SP_MAXSP => status.max_sp,
        _ => 0,
    };
    let p = PACKET_ZC_EL_PAR_CHANGE { packetType: PACKET_ZC_EL_PAR_CHANGE::ID as i16, r#type: type_ as u16, value };
    clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
}

/// clif.cpp clif_elemental_info: ZC_EL_INIT (0x81d) id.L hp.L maxhp.L sp.L maxsp.L to the master.
pub fn clif_elemental_info(c: &Ctx, aid: u32) {
    let Some(ed) = data::of_master(c, aid) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    let status = &ed.battle_status;
    let mut w = Vec::with_capacity(22);
    w.extend_from_slice(&0x081du16.to_le_bytes());
    w.extend_from_slice(&ed.id.to_le_bytes());
    w.extend_from_slice(&status.hp.to_le_bytes());
    w.extend_from_slice(&status.max_hp.to_le_bytes());
    w.extend_from_slice(&status.sp.to_le_bytes());
    w.extend_from_slice(&status.max_sp.to_le_bytes());
    clif::send(c, &w, &sd, Target::SelfOnly);
}

/// clif.cpp clif_hpmeter_single(*sd, id, hp, maxhp) to the master `aid`.
pub fn clif_hpmeter_single(c: &Ctx, aid: u32, id: u32, hp: u32, maxhp: u32) {
    if let Some(sd) = unit::get(c, aid) {
        crate::status::clif::hpmeter_single(c, &sd, id, hp, maxhp);
    }
}
