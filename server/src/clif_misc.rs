//! clif.cpp packet senders the general script commands (script2) need: overhead chat (clif_disp_overhead_), progress
//! bars (clif_progressbar, clif_progressbar_abort, clif_progressbar_npc), hat effects (clif_hat_effects,
//! clif_hat_effect_single) and the NPC extras of clif_spawn / clif_getareachar_unit that go with them.

use crate::clif::{self, Target};
use crate::map::BL_PC;
use crate::net::packets::*;
use crate::unit::Unit;
use crate::world::Ctx;

/// clif.cpp clif_disp_overhead_: ZC_NOTIFY_CHAT (0x008d) "message" above `bl` to the players around it
/// (AREA_CHAT_WOC) when `flag` is AREA, then ZC_NOTIFY_PLAYERCHAT (0x008e) back to the speaker when it is a player.
/// `mes` is the message without its NUL; rAthena's 256-byte buffer caps the text at 247 bytes + NUL.
pub fn disp_overhead(c: &Ctx, bl: &Unit, mes: &[u8], flag: Target) {
    let mes: Vec<u8> = mes.iter().copied().take_while(|&b| b != 0).collect();
    let mut len_mes = mes.len() + 1; // Account for \0
    if len_mes > 256 - 8 {
        log::error!("clif_disp_overhead: Message too long (length {len_mes})");
        len_mes = 256 - 8; // Trunk it to avoid problems.
    }
    // safestrncpy(WBUFCP(buf,8), mes, len_mes): len_mes - 1 characters and the NUL
    let mut body: Vec<u8> = mes.iter().copied().take(len_mes - 1).collect();
    body.push(0);
    // send message to others
    if flag == Target::Area {
        let mut w = Vec::with_capacity(len_mes + 8);
        w.extend_from_slice(&0x008du16.to_le_bytes());
        w.extend_from_slice(&((len_mes + 8) as u16).to_le_bytes());
        w.extend_from_slice(&bl.id.to_le_bytes());
        w.extend_from_slice(&body);
        clif::send(c, &w, bl, Target::AreaChatWoc);
    }
    // send back message to the speaker
    if bl.kind == BL_PC {
        let mut w = Vec::with_capacity(len_mes + 4);
        w.extend_from_slice(&0x008eu16.to_le_bytes());
        w.extend_from_slice(&((len_mes + 4) as u16).to_le_bytes());
        w.extend_from_slice(&body);
        clif::send(c, &w, bl, Target::SelfOnly);
    }
}

/// clif.cpp clif_progressbar: ZC_PROGRESS (0x02f0) color.L time.L to the player.
pub fn progressbar(c: &Ctx, sd: &Unit, color: u32, second: u32) {
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x02f0u16.to_le_bytes());
    w.extend_from_slice(&color.to_le_bytes());
    w.extend_from_slice(&second.to_le_bytes());
    clif::send(c, &w, sd, Target::SelfOnly);
}

/// clif.cpp clif_progressbar_abort: ZC_PROGRESS_CANCEL (0x02f2).
pub fn progressbar_abort(c: &Ctx, sd: &Unit) {
    clif::send(c, &0x02f2u16.to_le_bytes(), sd, Target::SelfOnly);
}

/// clif.cpp clif_progressbar_npc (PACKETVER >= 20130821): ZC_PROGRESS_ACTOR (0x09d1) id.L color.L time.L while the
/// NPC's bar runs — to one player (`sd`), or to the NPC's area (clif_progressbar_npc_area, `sd` None). The time is
/// the whole seconds left (client_tick of (timeout - now) / 1000).
pub fn progressbar_npc(c: &Ctx, nd: &Unit, sd: Option<&Unit>) {
    let m = crate::pc_misc::npcm(c, nd.id);
    if m.progressbar_timeout > 0 {
        let mut w = Vec::with_capacity(14);
        w.extend_from_slice(&0x09d1u16.to_le_bytes());
        w.extend_from_slice(&nd.id.to_le_bytes());
        w.extend_from_slice(&m.progressbar_color.to_le_bytes());
        w.extend_from_slice(&(((m.progressbar_timeout - c.tick()) / 1000) as u32).to_le_bytes());
        match sd {
            Some(sd) => clif::send(c, &w, sd, Target::SelfOnly),
            None => clif::send(c, &w, nd, Target::Area),
        }
    }
}

/// clif.cpp clif_hat_effects: ZC_EQUIPMENT_EFFECT (0x0a3b) with every hat effect of `bl` (status 1), to `target`
/// around `tbl`. Nothing on a nocostume map or when the unit shows none.
pub fn hat_effects(c: &Ctx, bl: &Unit, target: Target, tbl: &Unit) {
    if crate::npc::mapflag(c, bl.map, crate::npc::MF_NOCOSTUME) {
        return;
    }
    let effects = crate::pc_misc::hat_effects(c, bl.id);
    if effects.is_empty() {
        return;
    }
    let p = PACKET_ZC_EQUIPMENT_EFFECT {
        packetType: PACKET_ZC_EQUIPMENT_EFFECT::ID as i16,
        packetLength: 0,
        aid: bl.id,
        status: 1,
        effects: effects.iter().map(|&e| e as u16).collect(),
    };
    clif::send(c, &p.to_vec(), tbl, target);
}

/// clif.cpp clif_hat_effect_single: one hat effect switched on or off, to the unit's area.
pub fn hat_effect_single(c: &Ctx, bl: &Unit, effect_id: u16, enable: bool) {
    if crate::npc::mapflag(c, bl.map, crate::npc::MF_NOCOSTUME) {
        return;
    }
    let p = PACKET_ZC_EQUIPMENT_EFFECT {
        packetType: PACKET_ZC_EQUIPMENT_EFFECT::ID as i16,
        packetLength: 0,
        aid: bl.id,
        status: enable as i8,
        effects: vec![effect_id],
    };
    clif::send(c, &p.to_vec(), bl, Target::Area);
}

/// map.hpp e_size SZ_MEDIUM / SZ_BIG (unit size; the body effects EF_BABYBODY2 / EF_GIANTBODY2 show it).
pub const SZ_MEDIUM: u8 = 1;
pub const SZ_BIG: u8 = 2;

fn size_effect(size: u8) -> Option<i32> {
    match size {
        SZ_BIG => crate::data::constant("EF_GIANTBODY2").map(|v| v as i32),
        SZ_MEDIUM => crate::data::constant("EF_BABYBODY2").map(|v| v as i32),
        _ => None,
    }
}

/// clif.cpp clif_spawn, BL_NPC: the size effect before the status icons (clif_efst_status_change_sub).
pub fn spawn_npc_size(c: &Ctx, nd: &Unit) {
    if let Some(ef) = size_effect(nd.size) {
        clif::specialeffect(c, nd, ef, Target::Area);
    }
}

/// clif.cpp clif_getareachar_unit, BL_NPC: the size effect to the viewer, before the status icons.
pub fn getareachar_npc_size(c: &Ctx, sd: &Unit, nd: &Unit) {
    if let Some(ef) = size_effect(nd.size) {
        clif::specialeffect_single(c, nd, ef, sd);
    }
}
