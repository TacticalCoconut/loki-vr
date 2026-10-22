//! Client effects and UI commands: classchange, misceffect, playBGMall, soundeffectall, removespecialeffect /
//! removespecialeffect2, showdigit, readbook, camerainfo, opentips, specialpopup, setdialogalign / setdialogsize /
//! setdialogpos / setdialogpospercent, showscript, globalmes. PACKETVER 20211103 (RE): specialpopup's packet needs
//! 20221005 and the dialog window size / position packets need a main client of 20220504, so those commands check
//! their arguments and send nothing, as rAthena's build does.

use crate::clif::{self, Target};
use crate::map::BL_PC;
use crate::npc::npc as _;
use crate::pc::pc as _;
use spacetimedb::Table;
use crate::script::buildins::*;
use crate::script::npc_run;
use crate::script::vm::*;
use crate::unit::{self, Unit};
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "classchange" => classchange,
        "misceffect" => misceffect,
        "playBGMall" => play_bgm_all,
        "soundeffectall" => soundeffectall,
        "removespecialeffect" => removespecialeffect,
        "showdigit" => showdigit,
        "readbook" => readbook,
        "camerainfo" => camerainfo,
        "opentips" => opentips,
        "specialpopup" => specialpopup,
        "setdialogalign" => setdialogalign,
        "setdialogsize" => setdialogsize,
        "setdialogpos" => setdialogpos,
        "setdialogpospercent" => setdialogpospercent,
        "showscript" => showscript,
        "globalmes" => globalmes,
        _ => return None,
    })
}

/// script.hpp EF_NONE / EF_MAX (enum e_effect: EF_SOUL_EXPLOSION = 1242 is the last).
const EF_NONE: i32 = -1;
const EF_MAX: i32 = 1243;
/// clif.hpp enum broadcast_flags BC_SELF.
const BC_SELF: i32 = 3;
/// clif.hpp enum out_ui_type OUT_UI_TIP.
const OUT_UI_TIP: i8 = 5;
/// map.hpp enum e_say_dialog_align: DIALOG_ALIGN_LEFT 0 … DIALOG_ALIGN_BOTTOM 5.
const DIALOG_ALIGN_LEFT: i32 = 0;
const DIALOG_ALIGN_BOTTOM: i32 = 5;

fn send_raw_self(c: &Ctx, aid: u32, w: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, w, &sd, Target::SelfOnly);
    }
}

/// map_mapname2mapid (-1: not on this server).
fn mapname2mapid(name: &[u8]) -> i32 {
    crate::data::maps().id_of(&lat(name)).map(|m| m as i32).unwrap_or(-1)
}

/// The players of map_foreachinallarea(…, m, x0, y0, x1, y1, BL_PC) (nothing for m < 0).
fn players_in_area(c: &Ctx, m: i32, x0: i32, y0: i32, x1: i32, y1: i32) -> Vec<Unit> {
    if m < 0 {
        return Vec::new();
    }
    crate::map::units_in_area(c, m as u16, x0 as i16, y0 as i16, x1 as i16, y1 as i16, BL_PC)
}

/// The players of map_foreachinmap(…, m, BL_PC).
fn players_on_map(c: &Ctx, m: i32) -> Vec<Unit> {
    if m < 0 {
        return Vec::new();
    }
    crate::map::players_on_map(c, m as u16)
}

/// classchange <view id>{,"<NPC name>"{,<flag>}}: clif_class_change (ZC_NPCSPRITE_CHANGE; player classes send
/// nothing) for the NPC, to the area or (BC_SELF) to the attached player only.
fn classchange(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let class = getnum(rt, st, 2);
    let nd_id = if st.hasdata(3) && !getstr(rt, st, 3).is_empty() {
        npc_run::npc_name2id(c, &lat(&getstr(rt, st, 3))).map(|n| n.id)
    } else {
        super::unitctl::map_id2bl(c, st.oid).map(|b| b.id())
    };
    let Some(nd_id) = nd_id else { return SCRIPT_CMD_FAILURE };
    let self_only = st.hasdata(4) && getnum(rt, st, 4) == BC_SELF;
    // (an NPC without a map has no unit row: clif_send to its area reaches nobody)
    let bl = unit::get(c, nd_id);
    if !self_only {
        if let Some(bl) = bl {
            crate::mob::clif::class_change(c, &bl, class, Target::Area, None);
        }
    } else {
        let Some(sd) = unit::get(c, st.rid).filter(|u| u.kind == BL_PC) else { return SCRIPT_CMD_FAILURE };
        if let Some(bl) = bl {
            crate::mob::clif::class_change(c, &bl, class, Target::SelfOnly, Some(&sd));
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// misceffect <effect>: clif_specialeffect around the NPC (or, from an item / fake NPC script, the player).
fn misceffect(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let kind = getnum(rt, st, 2);
    if kind <= EF_NONE || kind >= EF_MAX {
        log::error!("buildin_misceffect: unsupported effect id {kind}");
        return SCRIPT_CMD_FAILURE;
    }
    if st.oid != 0 && st.oid != crate::script::fake_npc_id(c) {
        if let Some(bl) = unit::get(c, st.oid) {
            clif::specialeffect(c, &bl, kind, Target::Area);
        }
    } else if let Some(aid) = rid2sd(rt, st, "buildin_misceffect") {
        if let Some(sd) = unit::get(c, aid) {
            clif::specialeffect(c, &sd, kind, Target::Area);
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// playBGMall "<BGM>"{,"<map>"{,<x0>,<y0>,<x1>,<y1>}}: clif_playBGM to every player of the area / map / server.
fn play_bgm_all(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let name = getstr(rt, st, 2);
    let players = if st.hasdata(7) {
        // specified part of map
        let m = mapname2mapid(&getstr(rt, st, 3));
        players_in_area(c, m, getnum(rt, st, 4), getnum(rt, st, 5), getnum(rt, st, 6), getnum(rt, st, 7))
    } else if st.hasdata(3) {
        // entire map
        players_on_map(c, mapname2mapid(&getstr(rt, st, 3)))
    } else {
        // entire server (map_foreachpc)
        let mut ids: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
        ids.sort_by_key(|&id| crate::party::idb_order(id));
        ids.into_iter().filter_map(|id| unit::get(c, id)).collect()
    };
    for sd in players {
        clif::play_bgm(c, &sd, &name);
    }
    SCRIPT_CMD_SUCCESS
}

/// soundeffectall "<file>",<type>{,"<map>"{,<x0>,<y0>,<x1>,<y1>}}: clif_soundeffect around the player (or NPC), or
/// to each player of the map / area (each hears it from itself).
fn soundeffectall(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let bl = if st.rid != 0 {
        match rid2sd(rt, st, "buildin_soundeffectall") {
            Some(aid) => unit::get(c, aid),
            None => unit::get(c, st.oid),
        }
    } else {
        unit::get(c, st.oid)
    };
    // (a floating NPC is found by map_id2bl but has no map: nothing is sent either way)
    if bl.is_none() && super::unitctl::map_id2bl(c, st.oid).is_none() {
        return SCRIPT_CMD_SUCCESS;
    }
    let name = getstr(rt, st, 2);
    let kind = getnum(rt, st, 3);
    if !st.hasdata(4) {
        // area around
        if let Some(bl) = bl {
            clif::soundeffect(c, &bl, &name, kind, Target::Area);
        }
    } else if !st.hasdata(5) {
        // entire map
        for sd in players_on_map(c, mapname2mapid(&getstr(rt, st, 4))) {
            clif::soundeffect(c, &sd, &name, kind, Target::SelfOnly);
        }
    } else if st.hasdata(8) {
        // specified part of map
        let m = mapname2mapid(&getstr(rt, st, 4));
        for sd in players_in_area(c, m, getnum(rt, st, 5), getnum(rt, st, 6), getnum(rt, st, 7), getnum(rt, st, 8)) {
            clif::soundeffect(c, &sd, &name, kind, Target::SelfOnly);
        }
    } else {
        log::error!("buildin_soundeffectall: insufficient arguments for specific area broadcast.");
    }
    SCRIPT_CMD_SUCCESS
}

/// clif.cpp clif_specialeffect_remove: ZC_REMOVE_EFFECT (0x0b0d) aid.L effect.L, to `target` around `to`.
fn clif_specialeffect_remove(c: &Ctx, src: &Unit, effect: i32, target: Target, to: &Unit) {
    let mut p = crate::net::packets::PACKET_ZC_REMOVE_EFFECT { packetType: crate::net::packets::PACKET_ZC_REMOVE_EFFECT::ID as i16, aid: src.id, effectId: effect as u32 };
    clif::send(c, &p.to_vec(), to, target);
    if crate::atcmd_disguise::disguised(c, src) {
        p.aid = crate::atcmd_disguise::disguised_bl_id(src.id);
        clif::send(c, &p.to_vec(), src, Target::SelfOnly);
    }
}

/// removespecialeffect <effect>{,<send target>{,"<NPC name>"}} / removespecialeffect2 <effect>{,<send target>{,"<player
/// name>"}}: ends a looping effect of the NPC (or player).
fn removespecialeffect(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let command = st.funcname.clone();
    let effect = getnum(rt, st, 2);
    if effect <= EF_NONE || effect >= EF_MAX {
        log::error!("buildin_{command}: unsupported effect id {effect}.");
        return SCRIPT_CMD_FAILURE;
    }
    let tnum = if st.hasdata(3) { getnum(rt, st, 3) } else { 2 }; // AREA
    let (src, to): (Option<Unit>, Option<Unit>);
    if command == "removespecialeffect" {
        let src_id = if !st.hasdata(4) {
            match super::unitctl::map_id2bl(c, st.oid) {
                Some(b) => b.id(),
                None => {
                    log::error!("buildin_{command}: npc not attached.");
                    return SCRIPT_CMD_FAILURE;
                }
            }
        } else {
            let name = getstr(rt, st, 4);
            match npc_run::npc_name2id(c, &lat(&name)) {
                Some(nd) => nd.id,
                None => {
                    log::error!("buildin_{command}: unknown npc {}.", lat(&name));
                    return SCRIPT_CMD_FAILURE;
                }
            }
        };
        src = unit::get(c, src_id);
        if tnum != 24 {
            to = src.clone();
        } else {
            let Some(aid) = rid2sd(rt, st, &format!("buildin_{command}")) else { return SCRIPT_CMD_FAILURE };
            to = unit::get(c, aid);
        }
    } else {
        let Some(aid) = nick2sd(rt, st, 4, &format!("buildin_{command}")) else { return SCRIPT_CMD_FAILURE };
        src = unit::get(c, aid);
        to = src.clone();
    }
    let (Some(src), Some(to)) = (src, to) else { return SCRIPT_CMD_SUCCESS };
    match super::super::dialog::target_of(tnum) {
        Some(t) => clif_specialeffect_remove(c, &src, effect, t, &to),
        None => log::warn!("buildin_{command}: send target {tnum} (party / guild / …) is not ported yet"),
    }
    SCRIPT_CMD_SUCCESS
}

/// showdigit <value>{,<type>}: ZC_SHOWDIGIT (0x01b1) type.B value.L (types 1 and 2 count from -|value|, type 3 shows
/// at most 99).
fn showdigit(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_showdigit") else { return SCRIPT_CMD_SUCCESS };
    let mut value = getnum(rt, st, 2);
    let kind: u32 = if st.hasdata(3) { getnum(rt, st, 3) as u32 } else { 0 };
    match kind {
        0 => {}
        1 | 2 => {
            // Use absolute value and then the negative value of it as starting value
            value = value.wrapping_abs().wrapping_neg();
        }
        3 => {
            value = value.wrapping_abs();
            if value > 99 {
                log::warn!("buildin_showdigit: type 3 can display 2 digits at max. Capping value {value} to 99...");
                reportsrc(rt, st);
                value = 99;
            }
        }
        _ => {
            log::error!("buildin_showdigit: Invalid type {kind}.");
            return SCRIPT_CMD_FAILURE;
        }
    }
    let mut w = Vec::with_capacity(7);
    w.extend_from_slice(&0x01b1u16.to_le_bytes());
    w.push(kind as u8);
    w.extend_from_slice(&value.to_le_bytes());
    send_raw_self(rt.c.unwrap(), aid, &w);
    SCRIPT_CMD_SUCCESS
}

/// readbook <book id>,<page>: ZC_READ_BOOK (0x0294) bookID.L page.L.
fn readbook(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_readbook") else { return SCRIPT_CMD_SUCCESS };
    let book_id = getnum(rt, st, 2);
    let page = getnum(rt, st, 3);
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x0294u16.to_le_bytes());
    w.extend_from_slice(&book_id.to_le_bytes());
    w.extend_from_slice(&page.to_le_bytes());
    send_raw_self(rt.c.unwrap(), aid, &w);
    SCRIPT_CMD_SUCCESS
}

/// camerainfo <range>,<rotation>,<latitude>{,<char id>}: ZC_CAMERA_INFO (0x0a78) show.B range.F rotation.F
/// latitude.F with the values / 100.
fn camerainfo(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 5, "buildin_camerainfo") else { return SCRIPT_CMD_FAILURE };
    let range = getnum(rt, st, 2) as f32 / 100.0f32;
    let rotation = getnum(rt, st, 3) as f32 / 100.0f32;
    let latitude = getnum(rt, st, 4) as f32 / 100.0f32;
    let mut w = Vec::with_capacity(15);
    w.extend_from_slice(&0x0a78u16.to_le_bytes());
    w.push(0); // show: false
    w.extend_from_slice(&range.to_le_bytes());
    w.extend_from_slice(&rotation.to_le_bytes());
    w.extend_from_slice(&latitude.to_le_bytes());
    send_raw_self(rt.c.unwrap(), aid, &w);
    SCRIPT_CMD_SUCCESS
}

/// opentips <tip id>{,<char id>}: clif_ui_open(OUT_UI_TIP).
fn opentips(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_opentips") else { return SCRIPT_CMD_FAILURE };
    let data = getnum(rt, st, 2);
    crate::itemui::clif_ui_open(rt.c.unwrap(), aid, OUT_UI_TIP, data);
    SCRIPT_CMD_SUCCESS
}

/// specialpopup <id>: clif_specialpopup, whose ZC_SPECIALPOPUP needs PACKETVER 20221005 (nothing is sent here).
fn specialpopup(rt: &Rt, st: &mut State) -> i32 {
    if rid2sd(rt, st, "buildin_specialpopup").is_none() {
        return SCRIPT_CMD_FAILURE;
    }
    let _id = getnum(rt, st, 2);
    SCRIPT_CMD_SUCCESS
}

/// setdialogalign <align>: ZC_DIALOG_TEXT_ALIGN (0x0ba1, PACKETVER_RE_NUM >= 20211103).
fn setdialogalign(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_setdialogalign") else { return SCRIPT_CMD_FAILURE };
    let align = getnum(rt, st, 2);
    if !(DIALOG_ALIGN_LEFT..=DIALOG_ALIGN_BOTTOM).contains(&align) {
        log::error!("buildin_setdialogalign: Unknown align value {align}");
        return SCRIPT_CMD_FAILURE;
    }
    let p = crate::net::packets::PACKET_ZC_DIALOG_TEXT_ALIGN { PacketType: crate::net::packets::PACKET_ZC_DIALOG_TEXT_ALIGN::ID as i16, align: align as u8 };
    send_raw_self(rt.c.unwrap(), aid, &p.to_vec());
    SCRIPT_CMD_SUCCESS
}

/// The argument checks of setdialogsize / setdialogpos / setdialogpospercent (their packets need a main client of
/// 2022-05-04: nothing is sent here). Without a player they push 0 (as rAthena does).
fn dialog_window(rt: &Rt, st: &mut State, func: &str, what: &str, max: i32) -> i32 {
    if rid2sd(rt, st, &format!("buildin_{func}")).is_none() {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    let x = getnum(rt, st, 2);
    if x < 0 || x > max {
        log::error!("buildin_{func}: x {what} {x} is out of range [0,{max}]");
        return SCRIPT_CMD_FAILURE;
    }
    let y = getnum(rt, st, 3);
    if y < 0 || y > max {
        log::error!("buildin_{func}: y {what} {y} is out of range [0,{max}]");
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

fn setdialogsize(rt: &Rt, st: &mut State) -> i32 {
    dialog_window(rt, st, "setdialogsize", "size", i16::MAX as i32)
}

fn setdialogpos(rt: &Rt, st: &mut State) -> i32 {
    dialog_window(rt, st, "setdialogpos", "position", i16::MAX as i32)
}

fn setdialogpospercent(rt: &Rt, st: &mut State) -> i32 {
    dialog_window(rt, st, "setdialogpospercent", "rate", 100)
}

/// showscript "<message>"{,<unit id>{,<send target>}}: ZC_SHOWSCRIPT (0x08b3) over the unit's head (the attached
/// player, else the NPC). Pushes 1.
fn showscript(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let msg = getstr(rt, st, 2);
    let mut id = 0i32;
    let bl = if st.hasdata(3) {
        id = getnum(rt, st, 3);
        super::unitctl::map_id2bl(c, id as u32)
    } else if st.rid != 0 {
        super::unitctl::map_id2bl(c, st.rid)
    } else {
        super::unitctl::map_id2bl(c, st.oid)
    };
    let Some(bl) = bl else {
        log::error!("buildin_showscript: Script not attached. (id={id}, rid={}, oid={})", st.rid as i32, st.oid as i32);
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let mut tnum = 2; // AREA
    if st.hasdata(4) {
        tnum = getnum(rt, st, 4);
        if tnum == 24 && crate::pc::get(c, bl.id()).is_none() {
            log::warn!("script: showscript: self can't be used for non-players objects.");
            return SCRIPT_CMD_FAILURE;
        }
    }
    if let Some(u) = bl.unit() {
        // clif_showscript: buf[256], the message cut to 248 bytes with its NUL
        let mut len = msg.len() + 1;
        if len > 256 - 8 {
            log::warn!("clif_showscript: Truncating too long message '{}' (len={len}).", lat(&msg));
            len = 256 - 8;
        }
        let mut w = Vec::with_capacity(8 + len);
        w.extend_from_slice(&0x08b3u16.to_le_bytes());
        w.extend_from_slice(&((len + 8) as u16).to_le_bytes());
        w.extend_from_slice(&u.id.to_le_bytes());
        w.extend_from_slice(&msg[..len - 1]);
        w.push(0);
        match super::super::dialog::target_of(tnum) {
            Some(t) => clif::send(c, &w, u, t),
            None => log::warn!("buildin_showscript: send target {tnum} (party / guild / …) is not ported yet"),
        }
    }
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// globalmes "<message>"{,"<NPC name>"}: npc_globalmessage — the named NPC (default: this NPC's name, looked up again
/// by name) says the message (99 bytes at most) to every player (clif_GlobalMessage, ALL_CLIENT).
fn globalmes(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let mes = getstr(rt, st, 2);
    let name = if st.hasdata(3) {
        lat(&getstr(rt, st, 3))
    } else {
        c.db().npc().id().find(st.oid).map(|n| n.name.clone()).unwrap_or_default()
    };
    // npc_globalmessage(name, mes)
    let Some(nd) = npc_run::npc_name2id(c, &name) else { return SCRIPT_CMD_SUCCESS };
    let temp: Vec<u8> = mes.iter().copied().take(99).collect(); // snprintf(temp, 100, "%s", mes)
    // clif_GlobalMessage(*nd, temp, ALL_CLIENT): ZC_NOTIFY_CHAT (0x008d) len.W gid.L message
    let len = temp.len() + 1;
    let mut w = Vec::with_capacity(8 + len);
    w.extend_from_slice(&0x008du16.to_le_bytes());
    w.extend_from_slice(&((8 + len) as u16).to_le_bytes());
    w.extend_from_slice(&nd.id.to_le_bytes());
    w.extend_from_slice(&temp);
    w.push(0);
    for p in c.db().pc().iter().filter(|p| !p.disconnected) {
        c.send(p.identity, &w);
    }
    SCRIPT_CMD_SUCCESS
}
