//! map/clif.cpp: the packets the map server sends. Each function names the clif function it ports; packet layouts
//! come from the generated rAthena structs (net/packets.rs).

use crate::map::{self, BL_ITEM, BL_MER, BL_MOB, BL_NPC, BL_PC, BL_PET, BL_SKILL};
use crate::pc::pc as _;
use spacetimedb::Table as _;
use crate::net::packets::*;
use crate::net::wire::{latin1, pos3, pos6};
use crate::unit::{self, Unit, CLR_DEAD, OPTION_CHASEWALK, OPTION_CLOAK, OPTION_HIDE, OPTION_INVISIBLE};
use crate::world::Ctx;

/// enum send_target (clif.hpp), the ones ported so far.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Target {
    AllClient,
    AllSamemap,
    Area,
    AreaWos,
    AreaWoc,
    AreaWosc,
    AreaChatWoc,
    SelfOnly,
    /// CLAN: the online members of the (player) source's clan (clan.rs).
    Clan,
}

/// clif.cpp clif_send.
pub fn send(c: &Ctx, buf: &[u8], bl: &Unit, target: Target) {
    match target {
        Target::AllClient => {
            for p in c.db().pc().iter().filter(|p| !p.disconnected) {
                c.send(p.identity, buf);
            }
        }
        Target::AllSamemap => {
            // every session whose character's map (tsd->m) is this one, clients still loading it included
            for p in c.db().pc().iter().filter(|p| !p.disconnected) {
                if unit::get(c, p.account_id).map(|u| u.map == bl.map).unwrap_or(false) {
                    c.send(p.identity, buf);
                }
            }
        }
        Target::Area | Target::AreaWosc | Target::AreaWoc | Target::AreaWos => {
            if matches!(target, Target::Area | Target::AreaWosc) && bl.kind == BL_PC && !bl.on_map {
                // Not in the block list yet: the source would miss its own packet.
                send(c, buf, bl, Target::SelfOnly);
            }
            let r = crate::conf::area_size();
            for u in map::units_in_area(c, bl.map, bl.x - r, bl.y - r, bl.x + r, bl.y + r, BL_PC) {
                send_sub(c, buf, &u, bl, target);
            }
        }
        Target::AreaChatWoc => {
            let r = crate::conf::area_size() - 5;
            for u in map::units_in_area(c, bl.map, bl.x - r, bl.y - r, bl.x + r, bl.y + r, BL_PC) {
                send_sub(c, buf, &u, bl, Target::AreaWoc);
            }
        }
        Target::SelfOnly => {
            if bl.kind == BL_PC {
                if let Some(id) = crate::pc::identity_of(c, bl.id) {
                    c.send(id, buf);
                }
            }
        }
        Target::Clan => {
            if bl.kind == BL_PC {
                crate::clan::send_clan(c, buf, bl.id);
            }
        }
    }
}

/// clif.cpp clif_send_sub for one player in the area.
fn send_sub(c: &Ctx, buf: &[u8], sd: &Unit, src: &Unit, target: Target) {
    let Some(p) = crate::pc::get(c, sd.id) else { return };
    if p.disconnected {
        return; // session_isActive
    }
    match target {
        Target::AreaWos => {
            if sd.id == src.id {
                return;
            }
        }
        Target::AreaWoc => {
            if p.chat_id != 0 || sd.id == src.id {
                return;
            }
        }
        Target::AreaWosc => {
            if src.kind == BL_PC {
                if let Some(sp) = crate::pc::get(c, src.id) {
                    if p.chat_id != 0 && p.chat_id == sp.chat_id {
                        return;
                    }
                }
            } else if src.kind == BL_NPC && p.chat_id != 0 && p.chat_id == crate::chat::npc_chat_id(c, src.id) {
                return;
            }
        }
        _ => {}
    }
    // Dynamic NPCs visible only to their owner (npc_is_hidden_dynamicnpc).
    if src.kind == BL_NPC && src.npc_owner_char != 0 && src.npc_owner_char != sd.char_id {
        return;
    }
    // update_enemy_position is on, so enemies' positions are never held back.
    c.send(p.identity, buf);
}

fn send_self(c: &Ctx, sd: &Unit, buf: &[u8]) {
    send(c, buf, sd, Target::SelfOnly);
}

/// pc.hpp pcdb_checkid: is this a player class?
pub fn pcdb_checkid(class: i32) -> bool {
    let c = class as u32;
    c < 31
        || (4001..=4052).contains(&c)
        || (4054..=4087).contains(&c)
        || (4096..=4112).contains(&c)
        || (4190..=4191).contains(&c)
        || (4211..=4212).contains(&c)
        || c == 4215
        || c == 4218
        || c == 4220
        || (4222..=4229).contains(&c)
        || (4238..=4244).contains(&c)
        || (4252..=4264).contains(&c)
        || (4278..=4281).contains(&c)
        || (4302..=4308).contains(&c)
        || c == 4316
}

/// clif.cpp clif_bl_type (PACKETVER >= 20170726) with the disguise test of its BL_PC case: a player disguised as a
/// monster / NPC is NPC_TYPE (0x1).
fn bl_type_of(c: &Ctx, u: &Unit, walking: bool) -> u8 {
    if u.kind == BL_PC {
        return (crate::atcmd_disguise::disguised(c, u) && !pcdb_checkid(u.vd.class)) as u8;
    }
    // clif_bl_type's BL_MOB special_state.ai arms (the merchant family's summons, wave E): AI_ABR 0xd, AI_BIONIC 0xe
    if u.kind == BL_MOB && !pcdb_checkid(u.vd.class) {
        match crate::mob::get(c, u.id).map(|md| md.special_ai) {
            Some(crate::mob::AI_ABR) => return 0xd, // NPC_ABR_TYPE
            Some(crate::mob::AI_BIONIC) => return 0xe, // NPC_BIONIC_TYPE
            _ => {}
        }
    }
    bl_type(u, walking)
}

/// clif.cpp clif_bl_type (PACKETVER >= 20170726); a player's disguise needs the context: bl_type_of.
pub fn bl_type(u: &Unit, walking: bool) -> u8 {
    match u.kind {
        BL_PC => 0x0,
        BL_ITEM => 0x2,
        BL_SKILL => 0x3,
        map::BL_CHAT => 0x4,
        BL_MOB => {
            if pcdb_checkid(u.vd.class) {
                0x0
            } else {
                // special_state.ai: AI_ABR 0xd, AI_BIONIC 0xe (read from mob_data by bl_type_of)
                0x5
            }
        }
        BL_NPC => {
            if pcdb_checkid(u.vd.class) && walking {
                0x0
            } else if crate::data::mobdb_checkid(u.vd.class) {
                0xC
            } else {
                0x6
            }
        }
        BL_PET => {
            if pcdb_checkid(u.vd.class) {
                0x0
            } else {
                0x7
            }
        }
        map::BL_HOM => 0x8,
        BL_MER => 0x9,
        map::BL_ELEM => 0xa,
        _ => 0x1,
    }
}

/// The unit packets' honor field: `(sd) ? sd->status.manner : 0`, read when the packet is built (the unit row's copy
/// is only the value at login; @mute, @mutearea and SC_NOCHAT's minutes change the character's).
fn honor(c: &Ctx, u: &Unit) -> i16 {
    if u.kind == BL_PC {
        crate::pc::character_of(c, u.id).map(|ch| ch.manner).unwrap_or(u.manner)
    } else {
        0
    }
}

/// clif.cpp clif_setlevel (clif.cpp:1025): `status_get_lv(bl)` — a player's level is its character's base level now
/// (the unit row's copy is the level at login: after a level change the entry packets other players receive kept
/// the old one; wave G's npc_events scenario), capped for the client where client_limit_unit_lv covers the type.
pub(crate) fn setlevel(c: &Ctx, u: &Unit) -> i16 {
    let lv = crate::status::status_get_lv(c, u);
    if crate::conf::client_limit_unit_lv() & u.kind != 0 {
        return (if lv < crate::conf::max_lv() {
            lv
        } else if lv < crate::conf::aura_lv() {
            crate::conf::max_lv() - 1
        } else {
            crate::conf::max_lv()
        }) as i16;
    }
    if u.kind == BL_NPC || u.kind == BL_PET {
        return 0;
    }
    lv as i16
}

/// body field (PACKETVER >= 20150513): 1 when LOOK_BODY2 is a second-job costume body.
fn body_flag(u: &Unit) -> u16 {
    (u.vd.body2 > 4331 && u.vd.body2 < 4350) as u16
}

fn hp_bar(c: &Ctx, u: &Unit) -> (i32, i32) {
    if crate::conf::monster_hp_bars_info() && u.kind == BL_MOB && u.hp < u.max_hp && !crate::npc::mapflag(c, u.map, crate::npc::MF_HIDEMOBHPBAR) {
        (u.max_hp as i32, u.hp as i32)
    } else {
        (-1, -1)
    }
}

/// A guild flag NPC shows its guild's emblem in the accessory fields (clif.cpp: "why flags work like this?"): the
/// emblem id, then GetWord(g_id, 1) / GetWord(g_id, 0). `ids` is the unit's (status_get_guild_id,
/// status_get_emblem_id) at send time (woe.rs unit_guild_ids).
fn accessories(u: &Unit, ids: (i32, i32)) -> (u16, u16, u16) {
    if u.kind == BL_NPC && u.vd.class == 722 {
        let g_id = ids.0 as u32;
        (ids.1 as u16, (g_id >> 16) as u16, (g_id & 0xffff) as u16)
    } else {
        (u.vd.head_bottom as u16, u.vd.head_top as u16, u.vd.head_mid as u16)
    }
}

/// clif.cpp clif_set_unit_idle (PACKETVER >= 20131223): the "standing" unit packet, to `target` around `tbl`.
pub fn set_unit_idle(c: &Ctx, u: &Unit, walking: bool, target: Target, tbl: &Unit) {
    let mut option = u.sc.option;
    if tbl.kind == BL_PC && u.kind == BL_NPC && u.vd.dead_sit == 0 {
        // npc option changed? — the viewer's cloaked NPCs (cloakonnpc script command, phase 5)
        if crate::pc::npc_cloaked_for(c, tbl.id, u.id) {
            option ^= OPTION_CLOAK;
        }
    }
    let ids = crate::woe::unit_guild_ids(c, u);
    let (acc, acc2, acc3) = accessories(u, ids);
    let (max_hp, hp) = hp_bar(c, u);
    let mut p = packet_idle_unit {
        PacketType: packet_idle_unit::ID as i16,
        PacketLength: 0,
        objecttype: bl_type_of(c, u, walking),
        AID: u.id,
        GID: if u.kind == BL_PC { u.char_id } else { 0 },
        speed: u.speed as i16,
        bodyState: u.sc.opt1,
        healthState: u.sc.opt2,
        effectState: option,
        job: u.vd.class as i16,
        head: u.vd.hair as u16,
        weapon: u.vd.weapon as u32,
        shield: u.vd.shield as u32,
        accessory: acc,
        accessory2: acc2,
        accessory3: acc3,
        headpalette: u.vd.hair_color as i16,
        bodypalette: u.vd.clothes_color as i16,
        headDir: if u.kind == BL_PC { u.head_dir as i16 } else { 0 },
        robe: u.vd.robe as u16,
        GUID: ids.0 as u32,
        GEmblemVer: ids.1 as i16,
        honor: honor(c, u),
        virtue: u.sc.opt3,
        isPKModeON: (u.kind == BL_PC && u.karma) as u8,
        sex: u.vd.sex,
        PosDir: pos3(u.x, u.y, unit::getdir(u)),
        xSize: if u.kind == BL_PC { 5 } else { 0 },
        ySize: if u.kind == BL_PC { 5 } else { 0 },
        state: u.vd.dead_sit,
        clevel: setlevel(c, u),
        font: if u.kind == BL_PC { u.font } else { 0 },
        maxHP: max_hp,
        HP: hp,
        isBoss: if u.kind == BL_MOB || u.kind == BL_PET { u.boss } else { 0 },
        body: body_flag(u),
        name: u.name.clone(),
    };
    send(c, &p.to_vec(), tbl, target);
    // if disguised, send to self
    if crate::atcmd_disguise::disguised(c, u) {
        p.objecttype = if pcdb_checkid(u.vd.class) { 0x0 } else { 0x5 }; // PC_TYPE : NPC_MOB_TYPE
        p.AID = crate::atcmd_disguise::disguised_bl_id(u.id);
        send(c, &p.to_vec(), u, Target::SelfOnly);
    }
}

/// clif.cpp clif_spawn_unit: the "appearing" unit packet.
fn spawn_unit(c: &Ctx, u: &Unit, target: Target) {
    let ids = crate::woe::unit_guild_ids(c, u);
    let (acc, acc2, acc3) = accessories(u, ids);
    let (max_hp, hp) = hp_bar(c, u);
    let mut p = packet_spawn_unit {
        PacketType: packet_spawn_unit::ID as i16,
        PacketLength: 0,
        objecttype: bl_type_of(c, u, false),
        AID: u.id,
        GID: if u.kind == BL_PC { u.char_id } else { 0 },
        speed: u.speed as i16,
        bodyState: u.sc.opt1,
        healthState: u.sc.opt2,
        effectState: u.sc.option,
        job: u.vd.class as i16,
        head: u.vd.hair as u16,
        weapon: u.vd.weapon as u32,
        shield: u.vd.shield as u32,
        accessory: acc,
        accessory2: acc2,
        accessory3: acc3,
        headpalette: u.vd.hair_color as i16,
        bodypalette: u.vd.clothes_color as i16,
        headDir: if u.kind == BL_PC { u.head_dir as i16 } else { 0 },
        robe: u.vd.robe as u16,
        GUID: ids.0 as u32,
        GEmblemVer: ids.1 as i16,
        honor: honor(c, u),
        virtue: u.sc.opt3,
        isPKModeON: (u.kind == BL_PC && u.karma) as u8,
        sex: u.vd.sex,
        PosDir: pos3(u.x, u.y, unit::getdir(u)),
        xSize: if u.kind == BL_PC { 5 } else { 0 },
        ySize: if u.kind == BL_PC { 5 } else { 0 },
        clevel: setlevel(c, u),
        font: if u.kind == BL_PC { u.font } else { 0 },
        maxHP: max_hp,
        HP: hp,
        isBoss: if u.kind == BL_MOB || u.kind == BL_PET { u.boss } else { 0 },
        body: body_flag(u) as i16,
        name: u.name.clone(),
    };
    if crate::atcmd_disguise::disguised(c, u) {
        // the others see the disguise, unless it is the player's own class; its own client gets it with the id negated
        let class = crate::pc::character_of(c, u.id).map(|ch| ch.class as i32).unwrap_or(0);
        if class != crate::atcmd_disguise::disguise_of(c, u.id) {
            send(c, &p.to_vec(), u, target);
        }
        p.objecttype = if pcdb_checkid(u.vd.class) { 0x0 } else { 0x5 }; // PC_TYPE : NPC_MOB_TYPE
        p.AID = crate::atcmd_disguise::disguised_bl_id(u.id);
        send(c, &p.to_vec(), u, Target::SelfOnly);
    } else {
        send(c, &p.to_vec(), u, target);
    }
}

/// clif.cpp clif_set_unit_walking: the "walking" unit packet, sent around `tsd` (or the walker).
fn set_unit_walking(c: &Ctx, u: &Unit, tsd: Option<&Unit>, target: Target) {
    let ids = crate::woe::unit_guild_ids(c, u);
    let (max_hp, hp) = hp_bar(c, u);
    let mut p = packet_unit_walking {
        PacketType: packet_unit_walking::ID as i16,
        PacketLength: 0,
        objecttype: bl_type_of(c, u, true),
        AID: u.id,
        GID: if u.kind == BL_PC { u.char_id } else { 0 },
        speed: u.speed as i16,
        bodyState: u.sc.opt1,
        healthState: u.sc.opt2,
        effectState: u.sc.option,
        job: u.vd.class as i16,
        head: u.vd.hair as u16,
        weapon: u.vd.weapon as u32,
        accessory: u.vd.head_bottom as u16,
        moveStartTime: c.tick() as u32,
        shield: u.vd.shield as u32,
        accessory2: u.vd.head_top as u16,
        accessory3: u.vd.head_mid as u16,
        headpalette: u.vd.hair_color as i16,
        bodypalette: u.vd.clothes_color as i16,
        headDir: if u.kind == BL_PC { u.head_dir as i16 } else { 0 },
        robe: u.vd.robe as u16,
        GUID: ids.0 as u32,
        GEmblemVer: ids.1 as i16,
        honor: honor(c, u),
        virtue: u.sc.opt3,
        isPKModeON: (u.kind == BL_PC && u.karma) as u8,
        sex: u.vd.sex,
        MoveData: pos6(u.x, u.y, u.ud.to_x, u.ud.to_y, u.ud.sx, u.ud.sy),
        xSize: if u.kind == BL_PC { 5 } else { 0 },
        ySize: if u.kind == BL_PC { 5 } else { 0 },
        clevel: setlevel(c, u),
        font: if u.kind == BL_PC { u.font } else { 0 },
        maxHP: max_hp,
        HP: hp,
        isBoss: if u.kind == BL_MOB || u.kind == BL_PET { u.boss } else { 0 },
        body: body_flag(u),
        name: u.name.clone(),
    };
    send(c, &p.to_vec(), tsd.unwrap_or(u), target);
    // if disguised, send the info to self
    if crate::atcmd_disguise::disguised(c, u) {
        p.objecttype = if pcdb_checkid(u.vd.class) { 0x0 } else { 0x5 }; // PC_TYPE : NPC_MOB_TYPE
        p.AID = crate::atcmd_disguise::disguised_bl_id(u.id);
        send(c, &p.to_vec(), u, Target::SelfOnly);
    }
}

/// Invisible units (JT_INVISIBLE) and NPCs hidden by the Maya Purple rule are never shown.
fn invisible_view(u: &Unit) -> bool {
    u.vd.class == 32767
}

/// clif.cpp clif_npc_mayapurple: an NPC with the hide option is shown only to intravision (phase 4 adds it).
fn mayapurple(u: &Unit) -> bool {
    u.kind == BL_NPC && u.invisible
}

/// clif.cpp clif_spawn: a unit appears in the area (to everyone but itself).
pub fn spawn(c: &Ctx, u: &Unit, walking: bool) {
    if invisible_view(u) || mayapurple(u) {
        return;
    }
    if u.kind == BL_NPC && u.vd.dead_sit == 0 {
        set_unit_idle(c, u, walking, Target::AreaWos, u);
    } else {
        spawn_unit(c, u, Target::AreaWos);
    }
    // (clif_refresh_clothcolor sends nothing for PACKETVER >= 20091103)
    match u.kind {
        // spirit spheres, Millennium Shield, soul / servant / abyss balls (skill/state.rs), tiny / big players
        // (sd->state.size: atcmd_pc), the battleground emblem (clif.cpp:1722-1723, clif_bg), spirit charms
        BL_PC => {
            crate::skill::state::spawn_pc_spheres(c, u);
            crate::atcmd_pc::size_effect_area(c, u);
            crate::clif_bg::spawn_pc(c, u);
            crate::skill::state::spawn_pc_charms(c, u);
            crate::status::clif::efst_status_change_sub(c, u, u, Target::Area);
        }
        BL_MOB => crate::mob::clif::spawn_mob(c, u),
        BL_NPC => {
            crate::clif_misc::spawn_npc_size(c, u);
            crate::status::clif::efst_status_change_sub(c, u, u, Target::Area);
            crate::clif_misc::progressbar_npc(c, u, None); // clif_progressbar_npc_area
        }
        // If the pet wears equip
        BL_PET => crate::pet::clif::spawn_pet(c, u, None),
        _ => {}
    }
    crate::clif_misc::hat_effects(c, u, Target::Area, u);
}

/// clif.cpp clif_getareachar_unit: tell `sd` about `bl` (it came into view).
pub fn getareachar_unit(c: &Ctx, sd: &Unit, bl: &Unit) {
    if bl.kind == BL_SKILL {
        return; // status_get_viewdata: a skill unit has no view data
    }
    if invisible_view(bl) || mayapurple(bl) {
        return;
    }
    if bl.kind == BL_NPC && bl.npc_owner_char != 0 && bl.npc_owner_char != sd.char_id {
        return;
    }
    if crate::conf::hide_cloaked_units() & bl.kind != 0 && bl.sc.option & (OPTION_HIDE | OPTION_CLOAK | OPTION_CHASEWALK | OPTION_INVISIBLE) != 0 {
        return;
    }
    if bl.ud.walktimer != 0 {
        set_unit_walking(c, bl, Some(sd), Target::SelfOnly);
    } else {
        set_unit_idle(c, bl, false, Target::SelfOnly, sd);
    }
    match bl.kind {
        BL_PC => {
            crate::pc::getareachar_pc(c, sd, bl);
            // tiny / big players (sd->state.size: atcmd_pc), then the battleground emblem (clif.cpp:5081-5082)
            crate::atcmd_pc::size_effect_single(c, bl, sd);
            crate::clif_bg::getareachar_pc(c, sd, bl);
            crate::status::clif::efst_status_change_sub(c, sd, bl, Target::SelfOnly);
        }
        BL_NPC => {
            crate::npc::getareachar_npc(c, sd, bl);
            crate::clif_misc::getareachar_npc_size(c, sd, bl);
            crate::status::clif::efst_status_change_sub(c, sd, bl, Target::SelfOnly);
            crate::clif_misc::progressbar_npc(c, bl, Some(sd));
        }
        BL_MOB => crate::mob::clif::getareachar_mob(c, sd, bl),
        // If the pet wears equip
        BL_PET => crate::pet::clif::spawn_pet(c, bl, Some(sd)),
        // Devotion Effects: `if( ((TBL_MER*)bl)->devotion_flag ) clif_devotion(bl, sd)` (mercenary/data.rs)
        BL_MER => {
            if crate::mercenary::data::get(c, bl.id).map(|md| md.devotion_flag).unwrap_or(false) {
                crate::skill::clif::clif_devotion(c, bl, Some(sd));
            }
        }
        _ => {}
    }
    crate::clif_misc::hat_effects(c, bl, Target::SelfOnly, sd);
}

/// clif.cpp clif_getareachar: one unit found around a player entering a map.
pub fn getareachar(c: &Ctx, sd: &Unit, bl: &Unit) {
    match bl.kind {
        BL_ITEM => crate::pc::getareachar_item(c, sd, bl),
        BL_SKILL => crate::skillunit::clif::getareachar_skill(c, sd, bl),
        _ => {
            if sd.id != bl.id {
                getareachar_unit(c, sd, bl);
            }
        }
    }
}

/// clif.cpp clif_outsight: `tbl` has gone out of view of `bl` (either may be a player).
pub fn outsight(c: &Ctx, bl: &Unit, tbl: &Unit) {
    if bl.id == tbl.id {
        return;
    }
    if tbl.kind == BL_PC && crate::pc::is_active_session(c, tbl.id) {
        // tbl lost sight of bl
        match bl.kind {
            BL_PC => {
                if !invisible_view(bl) {
                    clearunit_single(c, bl.id, 0, tbl);
                }
                crate::pc::outsight_extras(c, bl, tbl);
            }
            BL_ITEM => clearflooritem(c, bl, Some(tbl)),
            BL_SKILL => crate::skillunit::clif::outsight_skill(c, bl, tbl),
            BL_NPC => {
                if !mayapurple(bl) {
                    clearunit_single(c, bl.id, 0, tbl);
                }
            }
            _ => {
                if !invisible_view(bl) {
                    clearunit_single(c, bl.id, 0, tbl);
                }
            }
        }
    }
    if bl.kind == BL_PC && crate::pc::is_active_session(c, bl.id) {
        // bl is watching tbl go out of view
        if tbl.kind == BL_SKILL {
            //Trap knocked out of sight
            crate::skillunit::clif::outsight_skill(c, tbl, bl);
        } else if !invisible_view(tbl) && !mayapurple(tbl) {
            clearunit_single(c, tbl.id, 0, bl);
        }
    }
}

/// clif.cpp clif_insight: `tbl` has come into view of `bl`.
pub fn insight(c: &Ctx, bl: &Unit, tbl: &Unit) {
    if bl.id == tbl.id {
        return;
    }
    if tbl.kind == BL_PC && crate::pc::is_active_session(c, tbl.id) {
        match bl.kind {
            BL_ITEM => crate::pc::getareachar_item(c, tbl, bl),
            BL_SKILL => crate::skillunit::clif::getareachar_skill(c, tbl, bl),
            _ => getareachar_unit(c, tbl, bl),
        }
    }
    if bl.kind == BL_PC && crate::pc::is_active_session(c, bl.id) {
        getareachar_unit(c, bl, tbl);
    }
}

/// clif.cpp clif_clearunit_single: ZC_NOTIFY_VANISH to one player.
pub fn clearunit_single(c: &Ctx, gid: u32, clrtype: u8, tsd: &Unit) {
    let p = PACKET_ZC_NOTIFY_VANISH { packetType: PACKET_ZC_NOTIFY_VANISH::ID as i16, gid, r#type: clrtype };
    send(c, &p.to_vec(), tsd, Target::SelfOnly);
}

/// clif.cpp clif_clearunit_area: ZC_NOTIFY_VANISH to the area (the dead see their own death).
pub fn clearunit_area(c: &Ctx, bl: &Unit, clrtype: u8) {
    let mut p = PACKET_ZC_NOTIFY_VANISH { packetType: PACKET_ZC_NOTIFY_VANISH::ID as i16, gid: bl.id, r#type: clrtype };
    send(c, &p.to_vec(), bl, if clrtype == CLR_DEAD { Target::Area } else { Target::AreaWos });
    if crate::atcmd_disguise::disguised(c, bl) {
        p.gid = crate::atcmd_disguise::disguised_bl_id(bl.id);
        send(c, &p.to_vec(), bl, Target::SelfOnly);
    }
}

/// clif.cpp clif_clearflooritem.
pub fn clearflooritem(c: &Ctx, item: &Unit, tsd: Option<&Unit>) {
    let p = PACKET_ZC_ITEM_DISAPPEAR { packetType: PACKET_ZC_ITEM_DISAPPEAR::ID as i16, itemAid: item.id };
    match tsd {
        None => send(c, &p.to_vec(), item, Target::Area),
        Some(t) => send(c, &p.to_vec(), t, Target::SelfOnly),
    }
}

/// clif.cpp clif_walkok: ZC_NOTIFY_PLAYERMOVE to the walker.
pub fn walkok(c: &Ctx, sd: &Unit) {
    let p = PACKET_ZC_NOTIFY_PLAYERMOVE {
        packetType: PACKET_ZC_NOTIFY_PLAYERMOVE::ID as i16,
        moveStartTime: c.tick() as u32,
        moveData: pos6(sd.x, sd.y, sd.ud.to_x, sd.ud.to_y, 8, 8),
    };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_move: the walking packet to everyone around but the walker.
pub fn r#move(c: &Ctx, u: &Unit) {
    if invisible_view(u) || mayapurple(u) {
        return;
    }
    // clif_ally_only for hidden walkers only matters with update_enemy_position off (it is on).
    set_unit_walking(c, u, None, Target::AreaWos);
    // (clif_refresh_clothcolor sends nothing for PACKETVER >= 20091103) tiny / big players and monsters [Valaris]
    crate::atcmd_pc::size_effect_area(c, u);
    if u.kind == BL_PET {
        // If the pet wears equip
        crate::pet::clif::spawn_pet(c, u, None);
    }
}

/// clif.cpp clif_fixpos: ZC_STOPMOVE to the area.
pub fn fixpos(c: &Ctx, u: &Unit) {
    let mut p = PACKET_ZC_STOPMOVE { packetType: PACKET_ZC_STOPMOVE::ID as i16, AID: u.id, xPos: u.x as u16, yPos: u.y as u16 };
    send(c, &p.to_vec(), u, Target::Area);
    if crate::atcmd_disguise::disguised(c, u) {
        p.AID = crate::atcmd_disguise::disguised_bl_id(u.id);
        send(c, &p.to_vec(), u, Target::SelfOnly);
    }
}

/// clif.cpp clif_changed_dir.
pub fn changed_dir(c: &Ctx, u: &Unit, target: Target) {
    let mut p = PACKET_ZC_CHANGE_DIRECTION {
        packetType: PACKET_ZC_CHANGE_DIRECTION::ID as i16,
        srcId: u.id,
        headDir: if u.kind == BL_PC { u.head_dir as u16 } else { 0 },
        dir: unit::getdir(u),
    };
    send(c, &p.to_vec(), u, target);
    if crate::atcmd_disguise::disguised(c, u) {
        p.srcId = crate::atcmd_disguise::disguised_bl_id(u.id);
        p.headDir = 0;
        send(c, &p.to_vec(), u, Target::SelfOnly);
    }
}

/// clif.cpp clif_authok: ZC_ACCEPT_ENTER.
pub fn authok(c: &Ctx, sd: &Unit) {
    let p = PACKET_ZC_ACCEPT_ENTER {
        packetType: PACKET_ZC_ACCEPT_ENTER::ID as i16,
        startTime: c.tick() as u32,
        posDir: pos3(sd.x, sd.y, sd.ud.dir),
        xSize: 5,
        ySize: 5,
        font: sd.font as u16,
    };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_changemap: ZC_NPCACK_MAPMOVE (map_mapid2mapname: an instance map's source map).
pub fn changemap(c: &Ctx, sd: &Unit, m: u16, x: i16, y: i16) {
    let name = crate::instance_map::map_mapid2mapname(c, m as i32);
    let p = PACKET_ZC_NPCACK_MAPMOVE { packetType: PACKET_ZC_NPCACK_MAPMOVE::ID as i16, mapName: format!("{name}.gat"), xPos: x as u16, yPos: y as u16 };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_sitting / clif_standing: ZC_NOTIFY_ACT type 2 / 3.
pub fn sitting(c: &Ctx, u: &Unit) {
    act_simple(c, u, 2);
}
pub fn standing(c: &Ctx, u: &Unit) {
    act_simple(c, u, 3);
}
fn act_simple(c: &Ctx, u: &Unit, kind: u8) {
    let mut p = PACKET_ZC_NOTIFY_ACT { packetType: PACKET_ZC_NOTIFY_ACT::ID as i16, srcID: u.id as i32, r#type: kind, ..Default::default() };
    send(c, &p.to_vec(), u, Target::Area);
    if crate::atcmd_disguise::disguised(c, u) {
        p.srcID = crate::atcmd_disguise::disguised_bl_id(u.id) as i32;
        send(c, &p.to_vec(), u, Target::SelfOnly);
    }
}

/// clif.cpp clif_emotion: ZC_EMOTION to the area.
pub fn emotion(c: &Ctx, u: &Unit, kind: u8) {
    let p = PACKET_ZC_EMOTION { packetType: PACKET_ZC_EMOTION::ID as i16, GID: u.id as i32, r#type: kind };
    send(c, &p.to_vec(), u, Target::Area);
}

/// clif.cpp clif_displaymessage: each line of `mes` as ZC_NOTIFY_PLAYERCHAT (empty lines are not sent).
pub fn displaymessage(c: &Ctx, sd: &Unit, mes: &str) {
    for line in mes.split('\n') {
        let mut b = latin1(line);
        b.truncate(crate::net::CHAT_SIZE_MAX); // strnlen(line, CHAT_SIZE_MAX)
        if b.is_empty() {
            continue;
        }
        let mut w = Vec::with_capacity(5 + b.len());
        w.extend_from_slice(&0x008eu16.to_le_bytes());
        w.extend_from_slice(&((5 + b.len()) as u16).to_le_bytes());
        w.extend_from_slice(&b);
        w.push(0);
        send_self(c, sd, &w);
    }
}

/// clif.cpp clif_GlobalMessage: ZC_NOTIFY_CHAT "Name : message" around `bl`.
pub fn global_message(c: &Ctx, bl: &Unit, message: &str, target: Target) {
    let mut b = latin1(message);
    b.truncate(crate::net::CHAT_SIZE_MAX - 1);
    b.push(0);
    let p = PACKET_ZC_NOTIFY_CHAT { PacketType: PACKET_ZC_NOTIFY_CHAT::ID as i16, PacketLength: 0, GID: bl.id, Message: b };
    send(c, &p.to_vec(), bl, target);
}

/// clif.cpp clif_name for players and NPC-like units, to `target` around `src`.
pub fn name(c: &Ctx, src: &Unit, bl: &Unit, target: Target) {
    match bl.kind {
        BL_PC => {
            // Requesting your own "shadow" name. [Skotlex]
            let gid = if src.id == bl.id && target == Target::SelfOnly && crate::atcmd_disguise::disguised(c, bl) { -(bl.id as i32) } else { bl.id as i32 };
            // sd->fakename (@fakename): the name alone
            let fakename = crate::atcmd_pc::get(c, bl.id).fakename;
            if !fakename.is_empty() {
                let p = PACKET_ZC_ACK_REQNAMEALL { packet_id: PACKET_ZC_ACK_REQNAMEALL::ID, gid, name: fakename, ..Default::default() };
                send(c, &p.to_vec(), src, target);
                return;
            }
            let (party, guild, position, title) = crate::pc::name_parts(c, bl);
            let p = PACKET_ZC_ACK_REQNAMEALL {
                packet_id: PACKET_ZC_ACK_REQNAMEALL::ID,
                gid,
                name: bl.name.clone(),
                party_name: party,
                guild_name: guild,
                position_name: position,
                title_id: title as i32,
            };
            send(c, &p.to_vec(), src, target);
        }
        map::BL_HOM | BL_MER | BL_PET | BL_NPC | map::BL_ELEM => {
            let p = PACKET_ZC_ACK_REQNAMEALL_NPC {
                packet_id: PACKET_ZC_ACK_REQNAMEALL_NPC::ID,
                gid: bl.id as i32,
                name: bl.name.clone(),
                title: bl.ud.title.clone(),
                groupId: bl.ud.group_id,
            };
            send(c, &p.to_vec(), src, target);
        }
        BL_MOB => crate::mob::name(c, src, bl, target),
        _ => {}
    }
}

/// clif.cpp clif_name_area: a monster's name to everyone around it.
pub fn name_area(c: &Ctx, u: &Unit) {
    name(c, u, u, Target::Area);
}

/// clif.cpp clif_notify_time: ZC_NOTIFY_TIME.
pub fn notify_time(c: &Ctx, sd: &Unit) {
    let p = PACKET_ZC_NOTIFY_TIME { packetType: PACKET_ZC_NOTIFY_TIME::ID as i16, time: c.tick() as u32 };
    send_self(c, sd, &p.to_vec());
}

/// map_property values (clif.hpp).
pub const MAPPROPERTY_NOTHING: u16 = 0;
pub const MAPPROPERTY_FREEPVPZONE: u16 = 1;
pub const MAPPROPERTY_AGITZONE: u16 = 3; // (MAPPROPERTY_EVENTPVPZONE is 2)

/// clif.cpp clif_map_property (PACKETVER >= 20121010: 0x099b with the flag word).
pub fn map_property(c: &Ctx, sd: &Unit, property: u16, target: Target) {
    use crate::npc::*;
    let m = sd.map;
    let f = |flag| mapflag(c, m, flag) as u32;
    let gvg2 = mapflag_gvg2(c, m) as u32;
    let vs = mapflag_vs(c, m) as u32;
    // (sd && sd->duel_group > 0): a player in a duel sees the attack cursor and has lock-on disabled (duel.rs)
    let duel = (sd.kind == map::BL_PC && crate::duel::duel_group(c, sd.id) > 0) as u32;
    // getMapFlag answers the flag's value: the terms without `||` shift it (MF_BATTLEGROUND 2 sets bit 8, not bit 7)
    let v = |flag| mapflag_value(c, m, flag) as u32;
    let flags = (f(MF_PVP) | duel)
        | ((f(MF_BATTLEGROUND) | gvg2) << 1)
        | ((f(MF_BATTLEGROUND) | gvg2) << 2)
        | ((f(MF_FORCEMINEFFECT) | gvg2) << 3)
        | ((f(MF_NOLOCKON) | vs | duel) << 4)
        | (v(MF_PVP) << 5)
        | (v(MF_PARTYLOCK) << 6)
        | (v(MF_BATTLEGROUND) << 7)
        | (v(MF_NOCOSTUME) << 8)
        | ((1 - f(MF_NOUSECART)) << 9)
        | ((1 - f(MF_NOSUNMOONSTARMIRACLE)) << 10);
    let mut w = Vec::with_capacity(8);
    w.extend_from_slice(&0x099bu16.to_le_bytes());
    w.extend_from_slice(&property.to_le_bytes());
    w.extend_from_slice(&flags.to_le_bytes());
    send(c, &w, sd, target);
}

/// clif.cpp clif_disconnect_ack: 0x018b result.W.
pub fn disconnect_ack(c: &Ctx, sd: &Unit, result: i16) {
    let mut w = Vec::with_capacity(4);
    w.extend_from_slice(&0x018bu16.to_le_bytes());
    w.extend_from_slice(&result.to_le_bytes());
    send_self(c, sd, &w);
}

/// clif.cpp clif_charselectok: ZC_RESTART_ACK.
pub fn charselectok(c: &Ctx, sd: &Unit, ok: u8) {
    let p = PACKET_ZC_RESTART_ACK { packetType: PACKET_ZC_RESTART_ACK::ID as i16, r#type: ok };
    send_self(c, sd, &p.to_vec());
}


/// clif.cpp clif_sprite_change: ZC_SPRITE_CHANGE (LOOK_BODY2 is sent as the second-job costume flag).
pub fn sprite_change(c: &Ctx, bl: &Unit, id: u32, look: u8, val: i32, val2: i32, target: Target) {
    let val = if look == 13 { (val > 4331 && val < 4350) as i32 } else { val };
    let p = PACKET_ZC_SPRITE_CHANGE { packetType: 0x01d7, // sendLookType (PACKETVER >= 4)
        AID: id, r#type: look, val: val as u32, val2: val2 as u32 };
    send(c, &p.to_vec(), bl, target);
}

/// clif.cpp clif_wis_message: ZC_WHISPER to `to`. `sender` is the whispering player when there is one (it decides
/// senderGID and the GM crown); `mes` is sent with its NUL like rAthena's `mes_len` (strlen + 1).
pub fn wis_message(c: &Ctx, to: &Unit, nick: &str, sender: Option<&Unit>, mes: &str, gmlvl: i32) {
    let gmlvl = match sender {
        Some(s) => crate::pc::group_level(c, s.id),
        None => gmlvl,
    };
    let mut m = latin1(mes);
    m.truncate(crate::net::CHAT_SIZE_MAX - 1);
    m.push(0);
    let p = PACKET_ZC_WHISPER {
        PacketType: PACKET_ZC_WHISPER::ID as i16,
        PacketLength: 0,
        senderGID: sender.map(|s| s.id).unwrap_or(0),
        sender: nick.to_string(),
        isAdmin: (gmlvl == 99) as u8,
        message: m,
    };
    send_self(c, to, &p.to_vec());
}

/// clif.cpp clif_wis_end: ZC_ACK_WHISPER02 result (0 sent, 1 not online, 2 ignored, 3 everyone ignored) + own char id.
pub fn wis_end(c: &Ctx, sd: &Unit, result: u8) {
    let p = PACKET_ZC_ACK_WHISPER { packetType: PACKET_ZC_ACK_WHISPER::ID as i16, result, CID: sd.char_id };
    send_self(c, sd, &p.to_vec());
}

// ---------------------------------------------------------------------------------------------- NPC dialogs (phase 5)

/// Sends to every client (clif_send with ALL_CLIENT and no source unit: broadcasts).
pub fn send_all(c: &Ctx, buf: &[u8]) {
    for p in c.db().pc().iter().filter(|p| !p.disconnected) {
        c.send(p.identity, buf);
    }
}

/// clif.cpp clif_scriptmes: ZC_SAY_DIALOG (the message with its NUL).
pub fn scriptmes(c: &Ctx, sd: &Unit, npcid: u32, mes: &[u8]) {
    let mut m = mes.to_vec();
    m.push(0);
    let p = PACKET_ZC_SAY_DIALOG { PacketType: PACKET_ZC_SAY_DIALOG::ID as i16, PacketLength: 0, NpcID: npcid, message: m };
    send_self(c, sd, &p.to_vec());
}

/// clif.cpp clif_scriptnext: ZC_WAIT_DIALOG.
pub fn scriptnext(c: &Ctx, sd: &Unit, npcid: u32) {
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x00b5u16.to_le_bytes());
    w.extend_from_slice(&npcid.to_le_bytes());
    send_self(c, sd, &w);
}

/// clif.cpp clif_scriptclose: ZC_CLOSE_DIALOG.
pub fn scriptclose(c: &Ctx, sd: &Unit, npcid: u32) {
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x00b6u16.to_le_bytes());
    w.extend_from_slice(&npcid.to_le_bytes());
    send_self(c, sd, &w);
}

/// clif.cpp clif_scriptclear: ZC_CLEAR_DIALOG.
pub fn scriptclear(c: &Ctx, sd: &Unit, npcid: u32) {
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x08d6u16.to_le_bytes());
    w.extend_from_slice(&npcid.to_le_bytes());
    send_self(c, sd, &w);
}

/// clif.cpp clif_sendfakenpc: an invisible NPC (JT_HIDDEN_NPC: 111, npc.hpp e_job_types) at the player's cell, so the client accepts a
/// dialog from an NPC it cannot see (packet_idle_unit, objecttype 1, female, maxHP/HP -1).
pub fn sendfakenpc(c: &Ctx, sd: &Unit, npcid: u32) {
    let p = packet_idle_unit {
        PacketType: packet_idle_unit::ID as i16,
        PacketLength: 0,
        objecttype: 0x1,
        AID: npcid,
        GID: 0,
        speed: 0,
        bodyState: 0,
        healthState: 0,
        effectState: 0,
        job: 111, // JT_HIDDEN_NPC
        head: 0,
        weapon: 0,
        shield: 0,
        accessory: 0,
        accessory2: 0,
        accessory3: 0,
        headpalette: 0,
        bodypalette: 0,
        headDir: 0,
        robe: 0,
        GUID: 0,
        GEmblemVer: 0,
        honor: 0,
        virtue: 0,
        isPKModeON: 0,
        sex: 0,
        PosDir: pos3(sd.x, sd.y, sd.ud.dir),
        xSize: 5,
        ySize: 5,
        state: 0,
        clevel: 0,
        font: 0,
        maxHP: -1,
        HP: -1,
        isBoss: 0,
        body: 0,
        name: String::new(),
    };
    send_self(c, sd, &p.to_vec());
}

/// The fake-NPC test clif_scriptmenu / clif_scriptinput / clif_scriptinputstr share: the dialog NPC is the fake NPC
/// or out of the player's sight. Returns true when the fake NPC was sent (the caller records using_fake_npc).
fn dialog_needs_fakenpc(c: &Ctx, sd: &Unit, npcid: u32, using_fake_npc: bool, fake_id: u32) -> bool {
    if using_fake_npc {
        return false;
    }
    if npcid == fake_id {
        return true;
    }
    match unit::get(c, npcid) {
        Some(bl) => {
            let r = crate::conf::area_size();
            bl.map != sd.map || bl.x < sd.x - r - 1 || bl.x > sd.x + r + 1 || bl.y < sd.y - r - 1 || bl.y > sd.y + r + 1
        }
        None => false,
    }
}

/// clif.cpp clif_scriptmenu: ZC_MENU_LIST (fake NPC first when needed). Returns whether a fake NPC was sent.
pub fn scriptmenu(c: &Ctx, sd: &Unit, npcid: u32, mes: &[u8], using_fake_npc: bool, fake_id: u32) -> bool {
    let fake = dialog_needs_fakenpc(c, sd, npcid, using_fake_npc, fake_id);
    if fake {
        sendfakenpc(c, sd, npcid);
    }
    let mut m = mes.to_vec();
    m.push(0);
    let p = PACKET_ZC_MENU_LIST { packetType: PACKET_ZC_MENU_LIST::ID as i16, packetLength: 0, npcId: npcid, menu: m };
    send_self(c, sd, &p.to_vec());
    fake
}

/// clif.cpp clif_scriptinput: ZC_OPEN_EDITDLG.
pub fn scriptinput(c: &Ctx, sd: &Unit, npcid: u32, using_fake_npc: bool, fake_id: u32) -> bool {
    let fake = dialog_needs_fakenpc(c, sd, npcid, using_fake_npc, fake_id);
    if fake {
        sendfakenpc(c, sd, npcid);
    }
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x0142u16.to_le_bytes());
    w.extend_from_slice(&npcid.to_le_bytes());
    send_self(c, sd, &w);
    fake
}

/// clif.cpp clif_scriptinputstr: ZC_OPEN_EDITDLGSTR.
pub fn scriptinputstr(c: &Ctx, sd: &Unit, npcid: u32, using_fake_npc: bool, fake_id: u32) -> bool {
    let fake = dialog_needs_fakenpc(c, sd, npcid, using_fake_npc, fake_id);
    if fake {
        sendfakenpc(c, sd, npcid);
    }
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x01d4u16.to_le_bytes());
    w.extend_from_slice(&npcid.to_le_bytes());
    send_self(c, sd, &w);
    fake
}

/// clif.cpp clif_viewpoint: ZC_COMPASS.
pub fn viewpoint(c: &Ctx, sd: &Unit, npc_id: u32, kind: i32, x: u16, y: u16, id: i32, color: u32) {
    let mut w = Vec::with_capacity(23);
    w.extend_from_slice(&0x0144u16.to_le_bytes());
    w.extend_from_slice(&npc_id.to_le_bytes());
    w.extend_from_slice(&kind.to_le_bytes());
    w.extend_from_slice(&(x as i32).to_le_bytes());
    w.extend_from_slice(&(y as i32).to_le_bytes());
    w.push(id as u8);
    w.extend_from_slice(&color.to_le_bytes());
    send_self(c, sd, &w);
}

/// clif.cpp clif_cutin: ZC_SHOW_IMAGE2 (0x01b3, the image name in 64 bytes).
pub fn cutin(c: &Ctx, sd: &Unit, image: &[u8], kind: i32) {
    let mut w = Vec::with_capacity(67);
    w.extend_from_slice(&0x01b3u16.to_le_bytes());
    let mut name: Vec<u8> = image.iter().copied().take_while(|&b| b != 0).take(63).collect();
    name.resize(64, 0);
    w.extend_from_slice(&name);
    w.push(kind as u8);
    send_self(c, sd, &w);
}

/// clif.cpp clif_broadcast: ZC_BROADCAST (`len` counts the NUL; "blue" / "ssss" prefixes for BC_BLUE / BC_WOE).
/// `bl` None = every client (intif_broadcast's local send).
pub fn broadcast(c: &Ctx, bl: Option<&Unit>, mes: &[u8], len: usize, kind: i32, target: Target) {
    if len < 2 {
        return;
    }
    let mut m = Vec::new();
    if kind & 0x10 != 0 {
        m.extend_from_slice(b"blue");
    } else if kind & 0x20 != 0 {
        m.extend_from_slice(b"ssss");
    }
    // strncpy(dst, mes, len): the message, then NULs up to len
    let mut body: Vec<u8> = mes.iter().copied().take_while(|&b| b != 0).take(len).collect();
    body.resize(len, 0);
    m.extend_from_slice(&body);
    let p = PACKET_ZC_BROADCAST { packetType: PACKET_ZC_BROADCAST::ID as i16, PacketLength: 0, message: m };
    match bl {
        Some(bl) => send(c, &p.to_vec(), bl, target),
        None => send_all(c, &p.to_vec()),
    }
}

/// clif.cpp clif_broadcast2: ZC_BROADCAST2 with font formatting.
#[allow(clippy::too_many_arguments)]
pub fn broadcast2(c: &Ctx, bl: Option<&Unit>, mes: &[u8], len: usize, font_color: u32, font_type: i16, font_size: i16, font_align: i16, font_y: i16, target: Target) {
    if len < 2 {
        return;
    }
    let mut body: Vec<u8> = mes.iter().copied().take_while(|&b| b != 0).take(len).collect();
    body.resize(len, 0);
    let p = PACKET_ZC_BROADCAST2 {
        packetType: PACKET_ZC_BROADCAST2::ID as i16,
        PacketLength: 0,
        fontColor: font_color,
        fontType: font_type,
        fontSize: font_size,
        fontAlign: font_align,
        fontY: font_y,
        message: body,
    };
    match bl {
        Some(bl) => send(c, &p.to_vec(), bl, target),
        None => send_all(c, &p.to_vec()),
    }
}

/// clif.cpp clif_playBGM (PACKETVER 20211103: ZC_PLAY_NPC_BGM 0x07fe, 24-byte name).
pub fn play_bgm(c: &Ctx, sd: &Unit, name: &[u8]) {
    let mut w = Vec::with_capacity(26);
    w.extend_from_slice(&0x07feu16.to_le_bytes());
    let mut n: Vec<u8> = name.iter().copied().take_while(|&b| b != 0).take(23).collect();
    n.resize(24, 0);
    w.extend_from_slice(&n);
    send_self(c, sd, &w);
}

/// clif.cpp clif_soundeffect: ZC_SOUND (name 24B, act, term 0, AID of `bl`).
pub fn soundeffect(c: &Ctx, bl: &Unit, name: &[u8], kind: i32, target: Target) {
    let mut w = Vec::with_capacity(35);
    w.extend_from_slice(&0x01d3u16.to_le_bytes());
    let mut n: Vec<u8> = name.iter().copied().take_while(|&b| b != 0).take(23).collect();
    n.resize(24, 0);
    w.extend_from_slice(&n);
    w.push(kind as u8);
    w.extend_from_slice(&0u32.to_le_bytes());
    w.extend_from_slice(&bl.id.to_le_bytes());
    send(c, &w, bl, target);
}

/// clif.cpp clif_specialeffect: ZC_NOTIFY_EFFECT2 (0x01f3) around `bl` (a disguised player also gets it on its
/// disguise).
pub fn specialeffect(c: &Ctx, bl: &Unit, kind: i32, target: Target) {
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x01f3u16.to_le_bytes());
    w.extend_from_slice(&bl.id.to_le_bytes());
    w.extend_from_slice(&kind.to_le_bytes());
    send(c, &w, bl, target);
    if crate::atcmd_disguise::disguised(c, bl) {
        w[2..6].copy_from_slice(&crate::atcmd_disguise::disguised_bl_id(bl.id).to_le_bytes());
        send(c, &w, bl, Target::SelfOnly);
    }
}

/// clif.cpp clif_specialeffect_single: the same packet to one player.
pub fn specialeffect_single(c: &Ctx, bl: &Unit, kind: i32, to: &Unit) {
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x01f3u16.to_le_bytes());
    w.extend_from_slice(&bl.id.to_le_bytes());
    w.extend_from_slice(&kind.to_le_bytes());
    send_self(c, to, &w);
}

/// clif.cpp clif_messagecolor_target: 0x02c1 (color converted RGB→BGR when asked), to `target` around `bl` (or
/// around `sd` when given).
pub fn messagecolor_target(c: &Ctx, bl: &Unit, color: u32, msg: &[u8], rgb2bgr: bool, target: Target, sd: Option<&Unit>) {
    let w = messagecolor_packet(bl.id, color, msg, rgb2bgr);
    send(c, &w, sd.unwrap_or(bl), target);
}

/// clif_messagecolor_target's 0x02c1 from the object `id`.
pub fn messagecolor_packet(id: u32, color: u32, msg: &[u8], rgb2bgr: bool) -> Vec<u8> {
    let mut len = msg.iter().take_while(|&&b| b != 0).count() + 1;
    if len > net_chat_size() - 12 {
        log::warn!("clif_messagecolor: Truncating too long message (len={len}).");
        len = net_chat_size() - 12;
    }
    let color = if rgb2bgr { (color & 0x0000FF) << 16 | (color & 0x00FF00) | (color & 0xFF0000) >> 16 } else { color };
    let mut w = Vec::with_capacity(12 + len);
    w.extend_from_slice(&0x02c1u16.to_le_bytes());
    w.extend_from_slice(&((len + 12) as u16).to_le_bytes());
    w.extend_from_slice(&id.to_le_bytes());
    w.extend_from_slice(&color.to_le_bytes());
    let mut body: Vec<u8> = msg.iter().copied().take_while(|&b| b != 0).take(len).collect();
    body.resize(len, 0);
    w.extend_from_slice(&body);
    w
}

fn net_chat_size() -> usize {
    crate::net::CHAT_SIZE_MAX
}

/// clif.cpp clif_navigateTo: 0x08e2.
#[allow(clippy::too_many_arguments)]
pub fn navigate_to(c: &Ctx, sd: &Unit, mapname: &[u8], x: u16, y: u16, flag: u8, hide_window: bool, mob_id: u16) {
    let (mut x, mut y) = (x, y);
    let kind: u8 = if mob_id > 0 {
        x = 0;
        y = 0;
        3
    } else if x > 0 && y > 0 {
        0
    } else {
        x = 0;
        y = 0;
        1
    };
    let mut w = Vec::with_capacity(27);
    w.extend_from_slice(&0x08e2u16.to_le_bytes());
    w.push(kind);
    w.push(flag);
    w.push(hide_window as u8);
    let mut n: Vec<u8> = mapname.iter().copied().take_while(|&b| b != 0).take(15).collect();
    n.resize(16, 0);
    w.extend_from_slice(&n);
    w.extend_from_slice(&x.to_le_bytes());
    w.extend_from_slice(&y.to_le_bytes());
    w.extend_from_slice(&mob_id.to_le_bytes());
    send_self(c, sd, &w);
}

/// clif.cpp clif_changeoption_target(bl, target): ZC_STATE_CHANGE3 (0x0229) with the unit's option — to the area
/// (`to` None: then SC_PROVOKE's icon is sent again, as rAthena does) or one player.
pub fn changeoption(c: &Ctx, bl: &Unit, option: i32, to: Option<&Unit>) {
    let mut w = Vec::with_capacity(15);
    w.extend_from_slice(&0x0229u16.to_le_bytes());
    w.extend_from_slice(&bl.id.to_le_bytes());
    w.extend_from_slice(&bl.sc.opt1.to_le_bytes());
    w.extend_from_slice(&bl.sc.opt2.to_le_bytes());
    w.extend_from_slice(&option.to_le_bytes());
    w.push((bl.kind == BL_PC && bl.karma) as u8);
    // a disguised player is sent its disguise with the option, then itself (the real id) invisible
    let disguised = crate::atcmd_disguise::disguised(c, bl);
    let own = |w: &mut Vec<u8>, id: u32, option: i32| {
        w[2..6].copy_from_slice(&id.to_le_bytes());
        w[10..14].copy_from_slice(&option.to_le_bytes());
    };
    match to {
        None => {
            if disguised {
                send(c, &w, bl, Target::AreaWos);
                own(&mut w, crate::atcmd_disguise::disguised_bl_id(bl.id), option);
                send(c, &w, bl, Target::SelfOnly);
                own(&mut w, bl.id, OPTION_INVISIBLE);
                send(c, &w, bl, Target::SelfOnly);
            } else {
                send(c, &w, bl, Target::Area);
            }
            //Whenever we send "changeoption" to the client, the provoke icon is lost
            //There is probably an option for the provoke icon, but as we don't know it, we have to do this for now
            use crate::status::consts::SC_PROVOKE;
            if let Some(e) = crate::status::sc::get_sce(c, bl.id, SC_PROVOKE) {
                let tick = match crate::timer::due(c, e.timer) {
                    Some(due) if e.timer != 0 => due - c.tick(),
                    _ => crate::status::sc::INFINITE_TICK,
                };
                let icon = crate::db::misc::status::status_db().get_icon(SC_PROVOKE);
                crate::status::clif::status_change(c, bl, icon, 1, tick, 0, 0, 0);
            }
        }
        Some(t) => {
            if disguised {
                own(&mut w, crate::atcmd_disguise::disguised_bl_id(bl.id), option);
                send_self(c, t, &w);
                own(&mut w, bl.id, OPTION_INVISIBLE);
                send_self(c, t, &w);
            } else {
                send_self(c, t, &w);
            }
        }
    }
}
