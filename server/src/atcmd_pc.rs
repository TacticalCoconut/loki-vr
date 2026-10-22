//! The map_session_data fields (pc.hpp) that the generic @commands own (atcmd agent, wave D), kept in their own row
//! so no other system's writes collide with them, and the pc.cpp / clif.cpp / intif.cpp functions that go with them:
//! pc_isautolooting, intif_broadcast_obtain_special_item / clif_broadcast_obtain_special_item.
//! (sd->state.autoloot itself is script2's `pc_misc.autoloot`: the autoloot script command set it first.)
//!
//! The row is created on first use with pc_authok's defaults and dropped when the character leaves
//! (`unit_free`, called from pc::quit's unit_free part), as rAthena's map_session_data dies with the character.

use spacetimedb::{table, Table};

use crate::pc::pc as _;
use crate::world::Ctx;

/// config/core.hpp AUTOLOOTITEM_SIZE: max number of items on the @autolootid list.
pub const AUTOLOOTITEM_SIZE: usize = 10;

/// map_session_data fields of the @commands (pc.hpp).
#[table(accessor = pc_atcmd)]
#[derive(Clone, Debug, Default)]
pub struct PcAtcmd {
    #[primary_key]
    pub account_id: u32,
    /// sd->state.autolootid[AUTOLOOTITEM_SIZE] (@autolootid; 0 = free slot).
    pub autolootid: Vec<u32>,
    /// sd->state.autoloottype: 1 << item type (@autoloottype).
    pub autoloottype: u16,
    /// sd->state.autolooting: the @autolootid list is not empty (performance-saver).
    pub autolooting: bool,
    /// sd->state.showexp / showzeny (pc_authok: battle_config.disp_experience / disp_zeny, both off) and
    /// sd->state.showdelay (@showexp, @showzeny, @showdelay).
    pub showexp: bool,
    pub showzeny: bool,
    pub showdelay: bool,
    /// sd->state.monster_ignore (@monsterignore): monsters do not target the player.
    pub monster_ignore: bool,
    /// sd->state.noks (@ksprotection): 0 off, KSPROTECT_* type.
    pub noks: u8,
    /// sd->state.size (@size / @sizeall / @sizeguild): SZ_SMALL 0, SZ_MEDIUM 1, SZ_BIG 2; clif_spawn / clif_move /
    /// clif_getareachar_unit show it with EF_BABYBODY2 / EF_GIANTBODY2.
    pub size: u8,
    /// sd->fakename (@fakename): shown instead of the name by clif_name and in chat (clif_process_message).
    pub fakename: String,
    /// sd->disguise (@disguise, the disguise script command): the monster / NPC class the player looks like (0 none).
    pub disguise: i32,
}

/// The player's row, or pc_authok's defaults (not stored until something changes).
pub fn get(c: &Ctx, aid: u32) -> PcAtcmd {
    c.db().pc_atcmd().account_id().find(aid).unwrap_or_else(|| PcAtcmd {
        account_id: aid,
        autolootid: vec![0; AUTOLOOTITEM_SIZE],
        showexp: crate::conf::disp_experience(),
        showzeny: crate::inventory::disp_zeny(),
        // pc_authok: if (!(battle_config.display_skill_fail&2)) sd->state.showdelay = 1;
        showdelay: crate::skill::display_skill_fail() & 2 == 0,
        ..Default::default()
    })
}

/// clif.cpp clif_spawn / clif_move (BL_PC, BL_MOB): tiny / big units [Valaris] — EF_GIANTBODY2 / EF_BABYBODY2 to
/// the area (a player's sd->state.size, a monster's md->special_state.size).
pub fn size_effect_area(c: &Ctx, u: &crate::unit::Unit) {
    let size = match u.kind {
        crate::map::BL_PC => get(c, u.id).size as i32,
        crate::map::BL_MOB => crate::mob::get(c, u.id).map(|md| md.special_size as i32).unwrap_or(0),
        _ => return,
    };
    if let Some(e) = size_effect(size) {
        crate::clif::specialeffect(c, u, e, crate::clif::Target::Area);
    }
}

/// clif.cpp clif_getareachar_unit (BL_PC): the size effect of `bl` to the player `sd` only (clif_specialeffect_single).
pub fn size_effect_single(c: &Ctx, bl: &crate::unit::Unit, sd: &crate::unit::Unit) {
    if let Some(e) = size_effect(get(c, bl.id).size as i32) {
        crate::clif::specialeffect_single(c, bl, e, sd);
    }
}

fn size_effect(size: i32) -> Option<i32> {
    match size {
        2 => Some(crate::data::constant("EF_GIANTBODY2").unwrap_or(0) as i32), // SZ_BIG
        1 => Some(crate::data::constant("EF_BABYBODY2").unwrap_or(0) as i32),  // SZ_MEDIUM
        _ => None,
    }
}

pub fn put(c: &Ctx, r: &PcAtcmd) {
    if c.db().pc_atcmd().account_id().find(r.account_id).is_some() {
        c.db().pc_atcmd().account_id().update(r.clone());
    } else {
        c.db().pc_atcmd().insert(r.clone());
    }
}

/// unit.cpp unit_free (BL_PC): the character's map_session_data goes.
pub fn unit_free(c: &Ctx, aid: u32) {
    c.db().pc_atcmd().account_id().delete(aid);
}

/// pc.cpp pc_isautolooting: whether the item's type is on the @autoloottype list or the item on the @autolootid list.
pub fn pc_isautolooting(c: &Ctx, aid: u32, nameid: u32) -> bool {
    let r = get(c, aid);
    if r.autoloottype != 0 && r.autoloottype as u32 & (1u32 << crate::db::items::itemdb_type(nameid)) != 0 {
        return true;
    }
    if !r.autolooting {
        return false;
    }
    r.autolootid.iter().take(AUTOLOOTITEM_SIZE).any(|&id| id == nameid)
}

/// clif.hpp enum BROADCASTING_SPECIAL_ITEM_OBTAIN.
pub const ITEMOBTAIN_TYPE_BOXITEM: u8 = 0;
pub const ITEMOBTAIN_TYPE_MONSTER_ITEM: u8 = 1;
pub const ITEMOBTAIN_TYPE_NPC: u8 = 2;

/// intif.cpp intif_broadcast_obtain_special_item: the announcement on this map server (the copy for other map
/// servers through the char server has no receiver: the module is the only map server).
pub fn intif_broadcast_obtain_special_item(c: &Ctx, aid: u32, nameid: u32, sourceid: u32, kind: u8) {
    let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
    // Should not be here! (ITEMOBTAIN_TYPE_NPC → intif_broadcast_obtain_special_item_npc: the same local broadcast)
    if kind == ITEMOBTAIN_TYPE_NPC {
        clif_broadcast_obtain_special_item(c, &name, nameid, 0, ITEMOBTAIN_TYPE_NPC);
        return;
    }
    clif_broadcast_obtain_special_item(c, &name, nameid, sourceid, kind);
}

/// clif.cpp clif_broadcast_obtain_special_item to ALL_CLIENT: ZC_BROADCASTING_SPECIAL_ITEM_OBTAIN (0x07fd). The name
/// is hidden as broadcast_hide_name says (itemui's clif_hide_name). A box item (PACKETVER 20211103: the 39-byte form
/// without the refine level) names the box; a monster drop names the monster. rAthena quirk kept: for monster / NPC
/// items PacketLength = 12 + len + monsterNameLen (PACKETVER_RE_NUM >= 20180704) is one byte more than the packed
/// struct (59 bytes), so clif_send sends 60 bytes for a monster item (the byte after the struct: sent as 0 here) and
/// 36 for an NPC item.
pub fn clif_broadcast_obtain_special_item(c: &Ctx, char_name: &str, nameid: u32, container: u32, kind: u8) {
    use crate::db::items::client_nameid;
    const NAME_LENGTH: usize = 24;
    let name = crate::itemui::clif_hide_name(char_name);
    let mut w: Vec<u8> = Vec::with_capacity(60);
    w.extend_from_slice(&0x07fdu16.to_le_bytes());
    w.extend_from_slice(&0u16.to_le_bytes()); // PacketLength, below
    w.push(kind);
    w.extend_from_slice(&client_nameid(nameid).to_le_bytes());
    w.push(NAME_LENGTH as u8);
    let mut nb = crate::net::wire::latin1(&name);
    nb.truncate(NAME_LENGTH - 1); // safestrncpy
    nb.resize(NAME_LENGTH, 0);
    w.extend_from_slice(&nb);
    if kind == ITEMOBTAIN_TYPE_BOXITEM {
        w.push(4); // boxItemID_len = sizeof(BoxItemID)
        w.extend_from_slice(&client_nameid(container).to_le_bytes());
        let len = w.len() as u16; // sizeof(p) = 39
        w[2..4].copy_from_slice(&len.to_le_bytes());
        for p in c.db().pc().iter().filter(|p| !p.disconnected) {
            c.send(p.identity, &w);
        }
        return;
    }
    let monster_name_len = match kind {
        ITEMOBTAIN_TYPE_MONSTER_ITEM => {
            let mname = crate::mob::db_find(c, container as i32).map(|m| m.name.clone()).unwrap_or_default();
            let mut mb = crate::net::wire::latin1(&mname);
            mb.truncate(NAME_LENGTH - 1);
            mb.resize(NAME_LENGTH, 0);
            w.push(NAME_LENGTH as u8);
            w.extend_from_slice(&mb);
            NAME_LENGTH
        }
        ITEMOBTAIN_TYPE_NPC => {
            w.push(0);
            w.push(0); // monsterName[0] = '\0'
            0
        }
        _ => return, // (no other type)
    };
    let len = 12 + NAME_LENGTH + monster_name_len;
    w.resize(len, 0);
    w[2..4].copy_from_slice(&(len as u16).to_le_bytes());
    for p in c.db().pc().iter().filter(|p| !p.disconnected) {
        c.send(p.identity, &w);
    }
}

/// clif.cpp color_table[COLOR_LIGHT_GREEN]: 0xB5FFB5 (RGB→BGR at do_init_clif leaves it as it is).
pub const COLOR_LIGHT_GREEN: u32 = 0xB5FFB5;

/// printf("%0.2f") of a float promoted to double, as glibc prints it: two decimals, "inf" / "-inf", and "nan" /
/// "-nan" by the sign bit (x86's default NaN of 0.0f / 0.0f has it set).
fn c_f2(v: f32) -> String {
    if v.is_nan() {
        return if v.is_sign_negative() { "-nan".into() } else { "nan".into() };
    }
    if v.is_infinite() {
        return if v < 0.0 { "-inf".into() } else { "inf".into() };
    }
    crate::mob::cmd::c_fixed2(v as f64)
}

/// `a / (float)b` on x86: float division (b == 0 gives ±inf, 0 / 0 the default NaN, which has the sign bit set).
fn c_fdiv(a: u64, b: u64) -> f32 {
    if b == 0 && a == 0 {
        return -f32::NAN;
    }
    a as f32 / b as f32
}

/// pc.cpp pc_gainexp_disp: @showexp's line after gained or lost experience, in light green (msg 743: "Experience %s
/// Base:%ld (%0.2f%%) Job:%ld (%0.2f%%)", %s = msg 741 gained / 742 lost).
pub fn pc_gainexp_disp(c: &Ctx, aid: u32, base_exp: u64, next_base_exp: u64, job_exp: u64, next_job_exp: u64, lost: bool) {
    let sign: i64 = if lost { -1 } else { 1 };
    let fsign: f32 = if lost { -1.0 } else { 1.0 };
    let bp = c_fdiv(base_exp, next_base_exp) * 100.0 * fsign;
    let jp = c_fdiv(job_exp, next_job_exp) * 100.0 * fsign;
    let word = crate::data::msg_txt(if lost { 742 } else { 741 });
    let out = crate::data::format_msg(crate::data::msg_txt(743), &[&word, &((base_exp as i64).wrapping_mul(sign)), &c_f2(bp), &((job_exp as i64).wrapping_mul(sign)), &c_f2(jp)]);
    if let Some(u) = crate::unit::get(c, aid) {
        crate::clif::messagecolor_target(c, &u, COLOR_LIGHT_GREEN, &crate::net::wire::latin1(&out), false, crate::clif::Target::SelfOnly, None);
    }
}

/// map.hpp map_data fields of the @commands (one row per map that has one set; the map's defaults otherwise).
#[table(accessor = atcmd_map)]
#[derive(Clone, Debug, Default)]
pub struct AtcmdMap {
    #[primary_key]
    pub map: u16,
    /// mapdata->last_macrocheck: the tick of the map's last @macrochecker that reached a player (0: never).
    pub last_macrocheck: i64,
}

/// The map's row, or map_data's zeroed defaults.
pub fn map_get(c: &Ctx, m: u16) -> AtcmdMap {
    c.db().atcmd_map().map().find(m).unwrap_or(AtcmdMap { map: m, ..Default::default() })
}

pub fn map_put(c: &Ctx, r: &AtcmdMap) {
    if c.db().atcmd_map().map().find(r.map).is_some() {
        c.db().atcmd_map().map().update(r.clone());
    } else {
        c.db().atcmd_map().insert(r.clone());
    }
}
