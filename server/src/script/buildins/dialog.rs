//! NPC dialogs and messages: mes, next, clear, close / close2 / close3, menu, select, prompt, input, cutin, emotion,
//! npctalk, message, dispbottom, specialeffect(2), playBGM, soundeffect, viewpoint, navigateto, announce,
//! mapannounce, areaannounce (script.cpp BUILDIN_FUNCs of the same names).

use super::*;
use crate::clif::{self, Target};
use crate::script::npc_run::{self, put_sdx, sdx, NPCT_MENU, NPCT_WAIT};
use crate::script::reg;
use crate::script::vm::*;
use crate::unit::{self, Unit};

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "mes" => mes,
        "next" => next,
        "clear" => clear,
        "close" => close,
        "close2" => close2,
        "menu" => menu,
        "select" => select,
        "prompt" => prompt,
        "input" => input,
        "cutin" => cutin,
        "emotion" => emotion,
        "npctalk" => npctalk,
        "message" => message,
        "dispbottom" => dispbottom,
        "specialeffect" => specialeffect,
        "specialeffect2" => specialeffect2,
        "playBGM" => play_bgm,
        "soundeffect" => soundeffect,
        "viewpoint" => viewpoint,
        "navigateto" => navigateto,
        "announce" => announce,
        "mapannounce" => mapannounce,
        "areaannounce" => areaannounce,
        _ => return None,
    })
}

/// The attached player's unit (script_rid2sd + the unit row).
fn sd_unit(rt: &Rt, st: &mut State, func: &str) -> Option<Unit> {
    let aid = rid2sd(rt, st, func)?;
    rt.c.and_then(|c| unit::get(c, aid))
}

/// send_target by number (clif.hpp enum order), the ones clif.rs supports.
pub fn target_of(n: i32) -> Option<Target> {
    Some(match n {
        0 => Target::AllClient,
        1 => Target::AllSamemap,
        2 => Target::Area,
        3 => Target::AreaWos,
        4 => Target::AreaWoc,
        5 => Target::AreaWosc,
        6 => Target::AreaChatWoc,
        24 => Target::SelfOnly,
        _ => return None,
    })
}

/// mes.
fn mes(rt: &Rt, st: &mut State) -> i32 {
    let Some(sd) = sd_unit(rt, st, "buildin_mes") else { return SCRIPT_CMD_SUCCESS };
    let c = rt.c.unwrap();
    let mut i = 2;
    while st.hasdata(i) {
        let s = getstr(rt, st, i);
        clif::scriptmes(c, &sd, st.oid, &s);
        i += 1;
    }
    st.mes_active = true;
    SCRIPT_CMD_SUCCESS
}

/// next.
fn next(rt: &Rt, st: &mut State) -> i32 {
    if !st.mes_active {
        log::warn!("buildin_next: There is no mes active.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(sd) = sd_unit(rt, st, "buildin_next") else { return SCRIPT_CMD_SUCCESS };
    let c = rt.c.unwrap();
    let mut x = sdx(c, sd.id);
    x.npc_idle_type = NPCT_WAIT;
    put_sdx(c, &x);
    st.state = STOP;
    clif::scriptnext(c, &sd, st.oid);
    SCRIPT_CMD_SUCCESS
}

/// clear.
fn clear(rt: &Rt, st: &mut State) -> i32 {
    if !st.mes_active {
        log::warn!("buildin_clear: There is no mes active.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(sd) = sd_unit(rt, st, "buildin_clear") else { return SCRIPT_CMD_FAILURE };
    clif::scriptclear(rt.c.unwrap(), &sd, st.oid);
    SCRIPT_CMD_SUCCESS
}

/// close / close3.
fn close(rt: &Rt, st: &mut State) -> i32 {
    let Some(sd) = sd_unit(rt, st, "buildin_close") else { return SCRIPT_CMD_SUCCESS };
    // a dynamic NPC's last interaction (npc_load.rs)
    crate::npc_load::dynamicnpc_touch(rt.c.unwrap(), st.oid);
    let command = st.funcname.clone();
    if !st.mes_active {
        st.state = END; // keep backwards compatibility
        log::warn!("buildin_close: Incorrect use of '{command}' command!");
        reportsrc(rt, st);
    } else {
        st.state = CLOSE;
        st.mes_active = false;
    }
    if command == "close3" {
        st.clear_cutin = true;
    }
    clif::scriptclose(rt.c.unwrap(), &sd, st.oid);
    SCRIPT_CMD_SUCCESS
}

/// close2.
fn close2(rt: &Rt, st: &mut State) -> i32 {
    let Some(sd) = sd_unit(rt, st, "buildin_close2") else { return SCRIPT_CMD_SUCCESS };
    st.state = STOP;
    st.mes_active = false;
    clif::scriptclose(rt.c.unwrap(), &sd, st.oid);
    SCRIPT_CMD_SUCCESS
}

/// menu_countoptions: (valid options counted, total incremented).
fn menu_countoptions(s: &[u8], mut max_count: i32, total: &mut i32) -> i32 {
    let mut count = 0;
    let mut p = 0;
    *total += 1;
    while p < s.len() && s[p] == b':' {
        p += 1;
        *total += 1;
    }
    while p < s.len() {
        count += 1;
        max_count -= 1;
        if max_count == 0 {
            break;
        }
        while p < s.len() && s[p] != b':' {
            p += 1;
        }
        while p < s.len() && s[p] == b':' {
            p += 1;
            *total += 1;
        }
    }
    count
}

/// Sends the menu (cut to 2047 bytes like rAthena) and records the fake NPC when one was needed.
fn send_menu(rt: &Rt, st: &mut State, sd: &Unit, buf: &[u8], func: &str) {
    let c = rt.c.unwrap();
    let mut x = sdx(c, sd.id);
    let fake_id = npc_run::fake_npc_id(c);
    let fake = if buf.len() >= 2047 {
        let name = c.db().npc().id().find(st.oid).map(|n| n.name).unwrap_or_else(|| "Unknown".into());
        log::warn!("buildin_{func}: NPC Menu too long! (source:{name} / length:{})", buf.len());
        clif::scriptmenu(c, sd, st.oid, &buf[..2046], x.using_fake_npc, fake_id)
    } else {
        clif::scriptmenu(c, sd, st.oid, buf, x.using_fake_npc, fake_id)
    };
    if fake {
        x = sdx(c, sd.id);
        x.using_fake_npc = true;
        put_sdx(c, &x);
    }
}

use crate::npc::npc;

/// menu.
fn menu(rt: &Rt, st: &mut State) -> i32 {
    let Some(sd) = sd_unit(rt, st, "buildin_menu") else { return SCRIPT_CMD_SUCCESS };
    let c = rt.c.unwrap();
    let mut x = sdx(c, sd.id);
    x.npc_idle_type = NPCT_MENU;
    put_sdx(c, &x);
    if !x.menu_or_input {
        if st.lastdata() % 2 == 0 {
            log::error!("buildin_menu: Illegal number of arguments ({}).", st.lastdata() - 1);
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        let mut buf: Vec<u8> = Vec::new();
        let mut npc_menu = 0;
        let mut i = 2;
        while i < st.lastdata() {
            let text = getstr(rt, st, i);
            if !st.data(i + 1).is_label() {
                log::error!("buildin_menu: Argument #{i} (from 1) is not a label or label not found.");
                reportdata(&st.data(i + 1).clone());
                st.state = END;
                return SCRIPT_CMD_FAILURE;
            }
            if text.is_empty() {
                i += 2;
                continue;
            }
            if npc_menu > 0 {
                buf.push(b':');
            }
            buf.extend_from_slice(&text);
            let mut t = 0;
            npc_menu += menu_countoptions(&text, 0, &mut t);
            i += 2;
        }
        st.state = RERUNLINE;
        let mut x = sdx(c, sd.id);
        x.npc_menu = npc_menu;
        x.menu_or_input = true;
        put_sdx(c, &x);
        send_menu(rt, st, &sd, &buf, "menu");
        if npc_menu >= 0xff {
            log::warn!("buildin_menu: Too many options specified (current={npc_menu}, max=254).");
            reportsrc(rt, st);
        }
    } else if x.npc_menu == 0xff {
        // cancel was pressed
        x.menu_or_input = false;
        put_sdx(c, &x);
        st.state = END;
    } else {
        let mut menu = 0;
        x.menu_or_input = false;
        if x.npc_menu <= 0 {
            log::debug!("buildin_menu: Unexpected selection ({})", x.npc_menu);
            put_sdx(c, &x);
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        let mut i = 2;
        while i < st.lastdata() {
            let text = getstr(rt, st, i);
            x.npc_menu -= menu_countoptions(&text, x.npc_menu, &mut menu);
            if x.npc_menu <= 0 {
                break;
            }
            i += 2;
        }
        let left = x.npc_menu;
        put_sdx(c, &x);
        if left > 0 {
            log::debug!("buildin_menu: Selection is out of range ({left} pairs are missing?) - please report this");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        if !st.data(i + 1).is_label() {
            log::error!("buildin_menu: Unexpected data in label argument");
            reportdata(&st.data(i + 1).clone());
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        reg::pc_setreg(c, sd.id, "@menu", menu as i64);
        st.pos = getnum(rt, st, i + 1);
        st.state = GOTO;
    }
    SCRIPT_CMD_SUCCESS
}

/// select / prompt share their first call.
fn show_options(rt: &Rt, st: &mut State, sd: &Unit, func: &str) {
    let c = rt.c.unwrap();
    let mut buf: Vec<u8> = Vec::new();
    let mut npc_menu = 0;
    let mut i = 2;
    while i <= st.lastdata() {
        let text = getstr(rt, st, i);
        if npc_menu > 0 {
            buf.push(b':');
        }
        buf.extend_from_slice(&text);
        let mut t = 0;
        npc_menu += menu_countoptions(&text, 0, &mut t);
        i += 1;
    }
    st.state = RERUNLINE;
    let mut x = sdx(c, sd.id);
    x.npc_menu = npc_menu;
    x.menu_or_input = true;
    put_sdx(c, &x);
    send_menu(rt, st, sd, &buf, func);
    if npc_menu >= 0xff {
        log::warn!("buildin_{func}: Too many options specified (current={npc_menu}, max=254).");
        reportsrc(rt, st);
    }
}

/// The selected option's index among all options (grouped and empty ones counted).
fn selected_option(rt: &Rt, st: &mut State, sd: &Unit) -> i32 {
    let c = rt.c.unwrap();
    let mut x = sdx(c, sd.id);
    let mut menu = 0;
    x.menu_or_input = false;
    let mut i = 2;
    while i <= st.lastdata() {
        let text = getstr(rt, st, i);
        x.npc_menu -= menu_countoptions(&text, x.npc_menu, &mut menu);
        if x.npc_menu <= 0 {
            break;
        }
        i += 1;
    }
    put_sdx(c, &x);
    menu
}

/// select.
fn select(rt: &Rt, st: &mut State) -> i32 {
    let Some(sd) = sd_unit(rt, st, "buildin_select") else { return SCRIPT_CMD_SUCCESS };
    let c = rt.c.unwrap();
    let mut x = sdx(c, sd.id);
    x.npc_idle_type = NPCT_MENU;
    put_sdx(c, &x);
    if !x.menu_or_input {
        show_options(rt, st, &sd, "select");
    } else if x.npc_menu == 0xff {
        x.menu_or_input = false;
        put_sdx(c, &x);
        st.state = END;
    } else {
        let menu = selected_option(rt, st, &sd);
        reg::pc_setreg(c, sd.id, "@menu", menu as i64);
        st.pushint(menu as i64);
        st.state = RUN;
    }
    SCRIPT_CMD_SUCCESS
}

/// prompt.
fn prompt(rt: &Rt, st: &mut State) -> i32 {
    let Some(sd) = sd_unit(rt, st, "buildin_prompt") else { return SCRIPT_CMD_SUCCESS };
    let c = rt.c.unwrap();
    let mut x = sdx(c, sd.id);
    x.npc_idle_type = NPCT_MENU;
    put_sdx(c, &x);
    if !x.menu_or_input {
        show_options(rt, st, &sd, "prompt");
    } else if x.npc_menu == 0xff {
        x.menu_or_input = false;
        put_sdx(c, &x);
        reg::pc_setreg(c, sd.id, "@menu", 0xff);
        st.pushint(0xff);
        st.state = RUN;
    } else {
        let menu = selected_option(rt, st, &sd);
        reg::pc_setreg(c, sd.id, "@menu", menu as i64);
        st.pushint(menu as i64);
        st.state = RUN;
    }
    SCRIPT_CMD_SUCCESS
}

/// input.
fn input(rt: &Rt, st: &mut State) -> i32 {
    let Some(sd) = sd_unit(rt, st, "buildin_input") else { return SCRIPT_CMD_SUCCESS };
    let c = rt.c.unwrap();
    let Data::Name(id, idx, rf) = st.data(2).clone() else {
        log::error!("script:input: not a variable");
        reportdata(&st.data(2).clone());
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let name = name_of(id);
    let min = if st.hasdata(3) { getnum(rt, st, 3) } else { INPUT_MIN_VALUE };
    let max = if st.hasdata(4) { getnum(rt, st, 4) } else { INPUT_MAX_VALUE };
    let mut x = sdx(c, sd.id);
    x.npc_idle_type = NPCT_WAIT;
    put_sdx(c, &x);
    if !x.menu_or_input {
        x.menu_or_input = true;
        put_sdx(c, &x);
        st.state = RERUNLINE;
        let fake_id = npc_run::fake_npc_id(c);
        let fake = if is_string_variable(&name) {
            clif::scriptinputstr(c, &sd, st.oid, x.using_fake_npc, fake_id)
        } else {
            clif::scriptinput(c, &sd, st.oid, x.using_fake_npc, fake_id)
        };
        if fake {
            let mut x = sdx(c, sd.id);
            x.using_fake_npc = true;
            put_sdx(c, &x);
        }
    } else {
        x.menu_or_input = false;
        put_sdx(c, &x);
        if is_string_variable(&name) {
            let len = x.npc_str.len() as i32;
            reg::set_reg_str(rt, st, Some(sd.id), id, idx, &name, &x.npc_str, &rf);
            st.pushint(if len > max { 1 } else if len < min { -1 } else { 0 });
        } else {
            let amount = x.npc_amount;
            let v = if amount < min {
                min
            } else if amount > max {
                max
            } else {
                amount
            };
            reg::set_reg_num(rt, st, Some(sd.id), id, idx, &name, v as i64, &rf);
            st.pushint(if amount > max { 1 } else if amount < min { -1 } else { 0 });
        }
        st.state = RUN;
    }
    SCRIPT_CMD_SUCCESS
}

/// cutin.
fn cutin(rt: &Rt, st: &mut State) -> i32 {
    let Some(sd) = sd_unit(rt, st, "buildin_cutin") else { return SCRIPT_CMD_SUCCESS };
    let image = getstr(rt, st, 2);
    let kind = getnum(rt, st, 3);
    clif::cutin(rt.c.unwrap(), &sd, &image, kind);
    SCRIPT_CMD_SUCCESS
}

/// clif.hpp ET_MAX.
const ET_MAX: i32 = 88;

/// emotion.
fn emotion(rt: &Rt, st: &mut State) -> i32 {
    let kind = getnum(rt, st, 2);
    if !(0..ET_MAX).contains(&kind) {
        log::warn!("buildin_emotion: Unknown emotion {kind} (min=0, max={}).", ET_MAX - 1);
        return SCRIPT_CMD_FAILURE;
    }
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mut bl = None;
    if st.hasdata(3) {
        bl = rid2bl(rt, st, 3, "buildin_emotion");
        if bl.is_none() {
            log::warn!("buildin_emotion: Unknown game ID supplied {}.", getnum(rt, st, 3));
            return SCRIPT_CMD_FAILURE;
        }
    }
    let bl = bl.or_else(|| unit::get(c, st.oid));
    if let Some(bl) = bl {
        clif::emotion(c, &bl, kind as u8);
    }
    SCRIPT_CMD_SUCCESS
}

/// npctalk.
fn npctalk(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let s = getstr(rt, st, 2);
    let nd = if st.hasdata(3) && !getstr(rt, st, 3).is_empty() {
        let n = getstr(rt, st, 3);
        npc_run::npc_name2id(c, &lat(&n))
    } else {
        c.db().npc().id().find(st.oid)
    };
    let color = if st.hasdata(5) { getnum(rt, st, 5) as u32 } else { 0xFFFFFF };
    let Some(nd) = nd else { return SCRIPT_CMD_SUCCESS };
    let target = if st.hasdata(4) {
        match getnum(rt, st, 4) {
            0 => Target::AllClient,
            1 => Target::AllSamemap,
            3 => Target::SelfOnly,
            _ => Target::Area,
        }
    } else {
        Target::Area
    };
    let mut message = s;
    message.truncate(crate::net::CHAT_SIZE_MAX - 1);
    if target != Target::SelfOnly {
        match unit::get(c, nd.id) {
            Some(ndu) => clif::messagecolor_target(c, &ndu, color, &message, true, target, None),
            // a floating NPC (nd->m -1, no unit row): clif_send's ALL_CLIENT reaches every session, ALL_SAMEMAP and
            // AREA find nobody on map -1 (map_foreachinareaV returns at m < 0)
            None if target == Target::AllClient => clif::send_all(c, &clif::messagecolor_packet(nd.id, color, &message, true)),
            None => {}
        }
    } else {
        let Some(sd) = unit::get(c, st.rid).filter(|_| crate::pc::get(c, st.rid).is_some()) else { return SCRIPT_CMD_FAILURE };
        clif::send(c, &clif::messagecolor_packet(nd.id, color, &message, true), &sd, target);
    }
    SCRIPT_CMD_SUCCESS
}

/// message.
fn message(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let player = getstr(rt, st, 2);
    let msg = getstr(rt, st, 3);
    let Some(pl) = crate::pc::nick2sd(c, &lat(&player), false) else { return SCRIPT_CMD_SUCCESS };
    clif::displaymessage(c, &pl, &lat(&msg));
    SCRIPT_CMD_SUCCESS
}

/// clif.cpp color_table[COLOR_LIGHT_GREEN] (0xB5FFB5, BGR).
const COLOR_LIGHT_GREEN: u32 = 0xB5FFB5;

/// dispbottom.
fn dispbottom(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_dispbottom") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let msg = getstr(rt, st, 2);
    let Some(sd) = unit::get(c, aid) else { return SCRIPT_CMD_SUCCESS };
    if st.hasdata(3) {
        let color = getnum(rt, st, 3) as u32;
        clif::messagecolor_target(c, &sd, color, &msg, true, Target::SelfOnly, None);
    } else {
        clif::messagecolor_target(c, &sd, COLOR_LIGHT_GREEN, &msg, false, Target::SelfOnly, None);
    }
    SCRIPT_CMD_SUCCESS
}

/// script.hpp EF_NONE / EF_MAX.
const EF_NONE: i32 = -1;
const EF_MAX: i32 = 1243;

/// specialeffect.
fn specialeffect(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(mut bl) = unit::get(c, st.oid) else { return SCRIPT_CMD_SUCCESS };
    let kind = getnum(rt, st, 2);
    let tnum = if st.hasdata(3) { getnum(rt, st, 3) } else { 2 };
    if kind <= EF_NONE || kind >= EF_MAX {
        log::error!("buildin_specialeffect: unsupported effect id {kind}");
        return SCRIPT_CMD_FAILURE;
    }
    if st.hasdata(4) {
        let name = getstr(rt, st, 4);
        match npc_run::npc_name2id(c, &lat(&name)).and_then(|nd| unit::get(c, nd.id)) {
            Some(u) => bl = u,
            None => {
                log::error!("buildin_specialeffect: Unknown NPC \"{}\"", lat(&name));
                return SCRIPT_CMD_FAILURE;
            }
        }
    }
    if tnum == 24 {
        let Some(sd) = sd_unit(rt, st, "buildin_specialeffect") else { return SCRIPT_CMD_FAILURE };
        clif::specialeffect_single(c, &bl, kind, &sd);
    } else {
        match target_of(tnum) {
            Some(t) => clif::specialeffect(c, &bl, kind, t),
            None => log::warn!("buildin_specialeffect: send target {tnum} (party / guild / …) is not ported yet"),
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// specialeffect2.
fn specialeffect2(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = nick2sd(rt, st, 4, "buildin_specialeffect2") else { return SCRIPT_CMD_SUCCESS };
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let kind = getnum(rt, st, 2);
    let tnum = if st.hasdata(3) { getnum(rt, st, 3) } else { 2 };
    if kind <= EF_NONE || kind >= EF_MAX {
        log::error!("buildin_specialeffect2: unsupported effect id {kind}");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(sd) = unit::get(c, aid) else { return SCRIPT_CMD_SUCCESS };
    match target_of(tnum) {
        Some(t) => clif::specialeffect(c, &sd, kind, t),
        None => log::warn!("buildin_specialeffect2: send target {tnum} is not ported yet"),
    }
    SCRIPT_CMD_SUCCESS
}

/// playBGM.
fn play_bgm(rt: &Rt, st: &mut State) -> i32 {
    if let Some(sd) = sd_unit(rt, st, "buildin_playBGM") {
        let name = getstr(rt, st, 2);
        clif::play_bgm(rt.c.unwrap(), &sd, &name);
    }
    SCRIPT_CMD_SUCCESS
}

/// soundeffect.
fn soundeffect(rt: &Rt, st: &mut State) -> i32 {
    if let Some(sd) = sd_unit(rt, st, "buildin_soundeffect") {
        let name = getstr(rt, st, 2);
        let kind = getnum(rt, st, 3);
        clif::soundeffect(rt.c.unwrap(), &sd, &name, kind, Target::SelfOnly);
    }
    SCRIPT_CMD_SUCCESS
}

/// viewpoint.
fn viewpoint(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 7, "buildin_viewpoint") else {
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let kind = getnum(rt, st, 2);
    let x = getnum(rt, st, 3);
    let y = getnum(rt, st, 4);
    let id = getnum(rt, st, 5);
    let color = getnum(rt, st, 6);
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    if let Some(sd) = unit::get(c, aid) {
        clif::viewpoint(c, &sd, st.oid, kind, x as u16, y as u16, id, color as u32);
    }
    SCRIPT_CMD_SUCCESS
}

/// navigateto.
fn navigateto(rt: &Rt, st: &mut State) -> i32 {
    let mapname = getstr(rt, st, 2);
    let x = if st.hasdata(3) { getnum(rt, st, 3) as u16 } else { 0 };
    let y = if st.hasdata(4) { getnum(rt, st, 4) as u16 } else { 0 };
    let flag = if st.hasdata(5) { getnum(rt, st, 5) as u8 } else { 101 }; // NAV_KAFRA_AND_AIRSHIP
    let hide = if st.hasdata(6) { getnum(rt, st, 6) != 0 } else { true };
    let mob = if st.hasdata(7) { getnum(rt, st, 7) as u16 } else { 0 };
    let Some(aid) = charid2sd(rt, st, 8, "buildin_navigateto") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    if let Some(sd) = unit::get(c, aid) {
        clif::navigate_to(c, &sd, &mapname, x, y, flag, hide, mob);
    }
    SCRIPT_CMD_SUCCESS
}

/// clif.hpp broadcast_flags.
const BC_TARGET_MASK: i32 = 0x07;
const BC_SOURCE_MASK: i32 = 0x08;
const BC_NPC: i32 = 0x08;
const BC_COLOR_MASK: i32 = 0x30;
/// FW_NORMAL.
const FW_NORMAL: i32 = 400;

pub(crate) struct Font {
    color: Option<Vec<u8>>,
    kind: i16,
    size: i16,
    align: i16,
    y: i16,
}

pub(crate) fn font_args(rt: &Rt, st: &mut State, first: i32) -> Font {
    Font {
        color: if st.hasdata(first) { Some(getstr(rt, st, first)) } else { None },
        kind: if st.hasdata(first + 1) { getnum(rt, st, first + 1) } else { FW_NORMAL } as i16,
        size: if st.hasdata(first + 2) { getnum(rt, st, first + 2) } else { 12 } as i16,
        align: if st.hasdata(first + 3) { getnum(rt, st, first + 3) } else { 0 } as i16,
        y: if st.hasdata(first + 4) { getnum(rt, st, first + 4) } else { 0 } as i16,
    }
}

fn color_of(s: &[u8]) -> u32 {
    crate::script::compiler::strtoll(s, 0, 0).0 as u32
}

/// announce.
fn announce(rt: &Rt, st: &mut State) -> i32 {
    let mes = getstr(rt, st, 2);
    let flag = getnum(rt, st, 3);
    let font = font_args(rt, st, 4);
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let len = mes.len() + 1;
    if flag & (BC_TARGET_MASK | BC_SOURCE_MASK) != 0 {
        // Some(None): the script's NPC is floating (map_id2bl finds it, its m is -1, it has no unit row here)
        let bl = if flag & BC_NPC != 0 {
            match unit::get(c, st.oid) {
                Some(u) => Some(Some(u)),
                None => c.db().npc().id().find(st.oid).map(|_| None),
            }
        } else {
            charid2sd(rt, st, 9, "buildin_announce").and_then(|aid| unit::get(c, aid)).map(Some)
        };
        let Some(bl) = bl else { return SCRIPT_CMD_SUCCESS };
        let target = match flag & BC_TARGET_MASK {
            1 => Target::AllSamemap,
            2 => Target::Area,
            3 => Target::SelfOnly,
            _ => Target::AllClient,
        };
        match bl {
            Some(bl) => match &font.color {
                Some(fc) => clif::broadcast2(c, Some(&bl), &mes, len, color_of(fc), font.kind, font.size, font.align, font.y, target),
                None => clif::broadcast(c, Some(&bl), &mes, len, flag & BC_COLOR_MASK, target),
            },
            // clif_send from m -1: ALL_CLIENT reaches every session; ALL_SAMEMAP and AREA find nobody, SELF needs a
            // player source
            None if target == Target::AllClient => match &font.color {
                Some(fc) => clif::broadcast2(c, None, &mes, len, color_of(fc), font.kind, font.size, font.align, font.y, target),
                None => clif::broadcast(c, None, &mes, len, flag & BC_COLOR_MASK, target),
            },
            None => {}
        }
    } else {
        // intif_broadcast / intif_broadcast2: every local player (no other map servers)
        match &font.color {
            Some(fc) => clif::broadcast2(c, None, &mes, len, color_of(fc), font.kind, font.size, font.align, font.y, Target::AllClient),
            None => clif::broadcast(c, None, &mes, len, flag & BC_COLOR_MASK, Target::AllClient),
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// script.cpp buildin_announce_sub for each player (clif_broadcast2 with a font colour, else clif_broadcast; SELF).
pub(crate) fn announce_to(c: &crate::world::Ctx, players: Vec<Unit>, mes: &[u8], flag: i32, font: &Font) {
    let len = mes.len() + 1;
    for pl in players {
        match &font.color {
            Some(fc) => clif::broadcast2(c, Some(&pl), mes, len, color_of(fc), font.kind, font.size, font.align, font.y, Target::SelfOnly),
            None => clif::broadcast(c, Some(&pl), mes, len, flag & BC_COLOR_MASK, Target::SelfOnly),
        }
    }
}

/// mapannounce.
fn mapannounce(rt: &Rt, st: &mut State) -> i32 {
    let mapname = getstr(rt, st, 2);
    let mes = getstr(rt, st, 3);
    let flag = getnum(rt, st, 4);
    let font = font_args(rt, st, 5);
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(m) = crate::data::maps().id_of(&lat(&mapname)) else { return SCRIPT_CMD_SUCCESS };
    announce_to(c, crate::map::players_on_map(c, m), &mes, flag, &font);
    SCRIPT_CMD_SUCCESS
}

/// areaannounce.
fn areaannounce(rt: &Rt, st: &mut State) -> i32 {
    let mapname = getstr(rt, st, 2);
    let x0 = getnum(rt, st, 3);
    let y0 = getnum(rt, st, 4);
    let x1 = getnum(rt, st, 5);
    let y1 = getnum(rt, st, 6);
    let mes = getstr(rt, st, 7);
    let flag = getnum(rt, st, 8);
    let font = font_args(rt, st, 9);
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(m) = crate::data::maps().id_of(&lat(&mapname)) else { return SCRIPT_CMD_SUCCESS };
    let players = crate::map::units_in_area(c, m, x0 as i16, y0 as i16, x1 as i16, y1 as i16, crate::map::BL_PC);
    announce_to(c, players, &mes, flag, &font);
    SCRIPT_CMD_SUCCESS
}
