//! map/atcommand.cpp: groups, permissions, variables, battle flags, packets, reloads: @adjgroup, @addperm, @rmvperm,
//! @langtype, @set, @setbattleflag, @send, @reload and the @reload… commands (atcmd agent, wave D). Routed from
//! atcommand_atcmd.rs `run` by the ACMD_FUNC name.
//!
//! **What "reloading" means here.** rAthena re-reads a database or conf file from disk. This module embeds every
//! database and conf file at build time (tools/datagen.py → data/*.bin + data/json, data.rs datasets; conf.rs and each
//! system's conf constants), so re-reading gives exactly the data it already has: the "files" of a reload are always
//! unchanged. What a reload still does is everything around the re-read: it forgets the run-time changes rAthena
//! keeps in the in-memory database (script edits of item_db, addmonsterdrop, clones, item group pool counters, …),
//! and it runs the per-player / per-unit refreshes rAthena runs after the read (status recalculations, skill lists,
//! monster respawn packets, …). Each reload function below says which of these it has, and its message is sent
//! exactly as rAthena sends it.

use spacetimedb::Table;

use crate::atcommand_atcmd::Out;
use crate::cstr::Scan;
use crate::data::{format_msg, msg_txt};
use crate::net::wire::{from_latin1, latin1};
use crate::unit::Unit;
use crate::world::Ctx;

/// Runs `func` when it is one of this file's commands; None otherwise.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "adjgroup" => adjgroup(c, &out, sd, message),
        "addperm" => addperm(c, &out, sd, command, message),
        "rmvperm" => rmvperm(c, &out, sd, command, message),
        "langtype" => langtype(c, &out, sd, message),
        "set" => set(c, &out, sd, message),
        "setbattleflag" => setbattleflag(c, &out, message),
        "send" => send(c, &out, message),
        "reload" => reload(c, &out, sd, command, message),
        "reloaditemdb" => reloaditemdb(c, &out),
        "reloadcashdb" => reloadcashdb(c, &out),
        "reloadmobdb" => reloadmobdb(c, &out),
        "reloadskilldb" => reloadskilldb(c, &out),
        "reloadscript" => reloadscript(c, &out),
        "reloadatcommand" => reloadatcommand(c, &out),
        "reloadbattleconf" => reloadbattleconf(c, &out),
        "reloadstatusdb" => reloadstatusdb(&out),
        "reloadpcdb" => reloadpcdb(&out),
        "reloadmotd" => reloadmotd(&out),
        "reloadquestdb" => reloadquestdb(c, &out),
        "reloadmsgconf" => reloadmsgconf(&out),
        "reloadachievementdb" => reloadachievementdb(&out),
        "reloadattendancedb" => reloadattendancedb(&out),
        "reloadbarterdb" => reloadbarterdb(c, &out),
        "reloadlogconf" => reloadlogconf(&out),
        _ => return None,
    })
}

// ---------------------------------------------------------------------------------------------- shared helpers

/// mapit_getallusers / mapit_geteachpc / map_foreachpc: every session in pc_db, in its DBIterator order (the three
/// iterate pc_db in e985006: map.hpp `mapit_alloc(MAPIT_NORMAL, BL_PC)`, map.cpp map_foreachpc).
fn pc_db_order(c: &Ctx) -> Vec<u32> {
    use crate::pc::pc as _;
    let mut v: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    v.sort_by_key(|&a| crate::party::idb_order(a));
    v
}

/// C strtoll(s, nullptr, 10): leading white space, a sign, decimal digits; LLONG_MAX / LLONG_MIN on overflow, 0 when
/// nothing converts.
fn c_strtoll(s: &[u8]) -> i64 {
    let mut p = 0;
    while p < s.len() && crate::cstr::is_space(s[p]) {
        p += 1;
    }
    let mut neg = false;
    if p < s.len() && (s[p] == b'+' || s[p] == b'-') {
        neg = s[p] == b'-';
        p += 1;
    }
    let mut v: i128 = 0;
    let mut any = false;
    while p < s.len() && s[p].is_ascii_digit() {
        any = true;
        if v <= i64::MAX as i128 + 1 {
            v = v * 10 + (s[p] - b'0') as i128;
        }
        p += 1;
    }
    if !any {
        return 0;
    }
    let v = if neg { -v } else { v };
    v.clamp(i64::MIN as i128, i64::MAX as i128) as i64
}

// ---------------------------------------------------------------------------------------------- groups, permissions

/// pc_groups.hpp pc_g_permission_name[PC_PERM_MAX]: the permission names in table order.
pub(crate) const PC_G_PERMISSION_NAME: [(&str, u32); 31] = {
    use crate::rules::*;
    [
        ("can_trade", PC_PERM_TRADE),
        ("can_party", PC_PERM_PARTY),
        ("all_skill", PC_PERM_ALL_SKILL),
        ("all_equipment", PC_PERM_USE_ALL_EQUIPMENT),
        ("skill_unconditional", PC_PERM_SKILL_UNCONDITIONAL),
        ("join_chat", PC_PERM_JOIN_ALL_CHAT),
        ("kick_chat", PC_PERM_NO_CHAT_KICK),
        ("hide_session", PC_PERM_HIDE_SESSION),
        ("who_display_aid", PC_PERM_WHO_DISPLAY_AID),
        ("hack_info", PC_PERM_RECEIVE_HACK_INFO),
        ("any_warp", PC_PERM_WARP_ANYWHERE),
        ("view_hpmeter", PC_PERM_VIEW_HPMETER),
        ("view_equipment", PC_PERM_VIEW_EQUIPMENT),
        ("use_check", PC_PERM_USE_CHECK),
        ("use_changemaptype", PC_PERM_USE_CHANGEMAPTYPE),
        ("all_commands", PC_PERM_USE_ALL_COMMANDS),
        ("receive_requests", PC_PERM_RECEIVE_REQUESTS),
        ("show_bossmobs", PC_PERM_SHOW_BOSS),
        ("disable_pvm", PC_PERM_DISABLE_PVM),
        ("disable_pvp", PC_PERM_DISABLE_PVP),
        ("disable_commands_when_dead", PC_PERM_DISABLE_CMD_DEAD),
        ("channel_admin", PC_PERM_CHANNEL_ADMIN),
        ("can_trade_bounded", PC_PERM_TRADE_BOUNDED),
        ("item_unconditional", PC_PERM_ITEM_UNCONDITIONAL),
        ("command_enable", PC_PERM_ENABLE_COMMAND),
        ("bypass_stat_onclone", PC_PERM_BYPASS_STAT_ONCLONE),
        ("bypass_max_stat", PC_PERM_BYPASS_MAX_STAT),
        ("attendance", PC_PERM_ATTENDANCE),
        ("macro_detect", PC_PERM_MACRO_DETECT),
        ("macro_register", PC_PERM_MACRO_REGISTER),
        ("trade_unconditional", PC_PERM_TRADE_UNCONDITIONAL),
    ]
};

/// pc_groups.cpp pc_group_pc_load: sd->group = the group of sd->group_id and sd->permissions = that group's
/// permissions, so the session's own changes (permission_add / permission_remove, @addperm / @rmvperm: pc_misc's
/// perm_set / perm_reset) are gone. A player whose group does not exist is kicked (set_eof).
pub(crate) fn pc_group_pc_load(c: &Ctx, aid: u32) {
    let Some(p) = crate::pc::get(c, aid) else { return };
    if crate::rules::group(p.group_id).is_none() {
        let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
        log::warn!("pc_group_pc_load: {name} (AID:{aid}) logged in with unknown group id ({})! kicking...", p.group_id);
        crate::session::set_eof(c, aid);
        return;
    }
    // sd->permissions = group->permissions (only a row that holds changes needs writing)
    let mut m = crate::pc_misc::pcm(c, aid);
    if m.perm_set != 0 || m.perm_reset != 0 {
        m.perm_set = 0;
        m.perm_reset = 0;
        crate::pc_misc::put_pcm(c, &m);
    }
}

/// ACMD_FUNC(adjgroup): @adjgroup <group_id>: the session moves to another group until it logs out (sd->group_id is
/// the login server's account value; nothing is saved, as in rAthena).
fn adjgroup(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(mut p) = crate::pc::get(c, sd) else { return -1 };
    // sscanf(message, "%11d", &new_group) != 1 (the long is stored into an int32)
    let b = latin1(message);
    let new_group = Scan::new(&b).int(11).map(|v| v as i32);
    let Some(new_group) = new_group.filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1226)); // Usage: @adjgroup <group_id>
        return -1;
    };
    // player_group_db.exists(new_group): the key is a uint32
    if !crate::rules::groups().iter().any(|g| g.id == new_group as u32) {
        out.say(msg_txt(1227)); // Specified group does not exist.
        return -1;
    }
    // sd->group_id = new_group (every group of conf/groups.yml has an id below 256, which Pc::group_id holds)
    p.group_id = new_group as u8;
    crate::pc::put(c, &p);
    pc_group_pc_load(c, sd); // update cache
    out.say(msg_txt(1228)); // Group changed successfully.
    // clif_displaymessage(sd->fd, …): the target's own client (the same one for @adjgroup, the player for #adjgroup)
    if let Some(u) = crate::unit::get(c, sd) {
        crate::clif::displaymessage(c, &u, msg_txt(1229)); // Your group has changed.
    }
    0
}

/// "-- Permission List" and every permission name (addperm / rmvperm).
fn say_permission_list(out: &Out) {
    out.say(msg_txt(1379)); // -- Permission List
    for (name, _) in PC_G_PERMISSION_NAME.iter() {
        out.say(&format!("- {name}"));
    }
}

/// ARR_FIND(0, perm_size, i, strcmpi(pc_g_permission_name[i].name, message) == 0).
fn find_permission(message: &str) -> Option<(&'static str, u32)> {
    PC_G_PERMISSION_NAME.iter().find(|(n, _)| crate::cstr::eq_ignore_case(n, message)).copied()
}

/// The target's sd->status.name.
fn char_name(c: &Ctx, sd: u32) -> String {
    crate::pc::character_of(c, sd).map(|ch| ch.name).unwrap_or_default()
}

/// ACMD_FUNC(addperm): @addperm <permission_name>: sd->permissions.set(perm) for this session.
fn addperm(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    if message.is_empty() {
        out.say(&format_msg(msg_txt(1378), &[&command])); // Usage: %s <permission_name>
        say_permission_list(out);
        return -1;
    }
    let Some((name, perm)) = find_permission(message) else {
        out.say(&format_msg(msg_txt(1380), &[&message])); // '%s' is unknown permission.
        say_permission_list(out);
        return -1;
    };
    let who = char_name(c, sd);
    if crate::pc_misc::pc_has_permission(c, sd, perm) {
        out.say(&format_msg(msg_txt(1381), &[&who, &name])); // User '%s' already possesses the '%s' permission.
        return -1;
    }
    let mut m = crate::pc_misc::pcm(c, sd);
    let bit = 1u64 << perm;
    m.perm_set |= bit;
    m.perm_reset &= !bit;
    crate::pc_misc::put_pcm(c, &m);
    out.say(&format_msg(msg_txt(1384), &[&who])); // User '%s' permissions updated successfully. The changes are temporary.
    0
}

/// ACMD_FUNC(rmvperm): @rmvperm <permission_name>: sd->permissions.reset(perm) for this session.
fn rmvperm(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    if message.is_empty() {
        out.say(&format_msg(msg_txt(1378), &[&command])); // Usage: %s <permission_name>
        say_permission_list(out);
        return -1;
    }
    let Some((name, perm)) = find_permission(message) else {
        out.say(&format_msg(msg_txt(1380), &[&message])); // '%s' is unknown permission.
        say_permission_list(out);
        return -1;
    };
    let who = char_name(c, sd);
    if !crate::pc_misc::pc_has_permission(c, sd, perm) {
        out.say(&format_msg(msg_txt(1382), &[&who, &name])); // User '%s' doesn't possess the '%s' permission.
        out.say(&format_msg(msg_txt(1383), &[&who])); // -- User '%s' Permissions
        for (n, p) in PC_G_PERMISSION_NAME.iter() {
            if crate::pc_misc::pc_has_permission(c, sd, *p) {
                out.say(&format!("- {n}"));
            }
        }
        return -1;
    }
    let mut m = crate::pc_misc::pcm(c, sd);
    let bit = 1u64 << perm;
    m.perm_reset |= bit;
    m.perm_set &= !bit;
    crate::pc_misc::put_pcm(c, &m);
    out.say(&format_msg(msg_txt(1384), &[&who])); // User '%s' permissions updated successfully. The changes are temporary.
    0
}

// ---------------------------------------------------------------------------------------------- langtype

/// common/msg_conf.hpp LANG_ENABLE: defined neither by config/core.hpp nor by the oracle's ./configure, so 0x000
/// (English only); LANG_MAX = LANG_THA + 1.
const LANG_ENABLE: u16 = 0x000;
const LANG_MAX: u16 = 0x101;
/// pc.hpp LANGTYPE_VAR.
const LANGTYPE_VAR: &[u8] = b"#langtype";

/// common/msg_conf.cpp msg_langstr2langtype: the first two letters, case-insensitively (strncmpi(…, 2)); -1 unknown.
fn msg_langstr2langtype(s: &[u8]) -> i32 {
    const LANGS: [&[u8]; 10] = [b"eng", b"rus", b"spn", b"grm", b"chn", b"mal", b"idn", b"frn", b"por", b"tha"];
    let m = |p: &[u8]| (0..2).all(|i| s.get(i).map(|c| c.to_ascii_lowercase()) == Some(p[i]));
    LANGS.iter().position(|p| m(p)).map(|i| i as i32).unwrap_or(-1)
}

/// common/msg_conf.cpp msg_langtype2langstr.
fn msg_langtype2langstr(lang: i32) -> &'static str {
    match lang {
        0 => "English (ENG)",
        1 => "Russkiy (RUS)",
        2 => "Espanol (SPN)",
        3 => "Deutsch (GRM)",
        4 => "Hanyu (CHN)",
        5 => "Bahasa Malaysia (MAL)",
        6 => "Bahasa Indonesia (IDN)",
        7 => "Francais (FRN)",
        8 => "Portugues Brasileiro (POR)",
        9 => "Thai (THA)",
        _ => "??",
    }
}

/// common/msg_conf.cpp msg_checklangtype: 1 enabled, -1 out of range, -2 disabled. `test` is `(uint16)(1 << (lang-1))`
/// (the shift count masked to 5 bits as x86 does).
fn msg_checklangtype(lang: i32) -> i32 {
    let test = 1u32.wrapping_shl(lang.wrapping_sub(1) as u32 & 31) as u16;
    if lang == 0 {
        return 1; // default english
    }
    if lang < 0 || test > LANG_MAX {
        return -1; // false range
    }
    if LANG_ENABLE & test != 0 {
        return 1;
    }
    -2
}

/// ACMD_FUNC(langtype): @langtype <language>. Only English is enabled (LANG_ENABLE 0), so the only language that can be
/// set is English: sd->langtype 0, which every player already has (pc_reg_received resets any invalid #langtype to 0,
/// so the module keeps no sd->langtype); #langtype is written as pc_setaccountreg does (0 removes the variable).
fn langtype(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    // sscanf(message, "%3s", langstr) >= 1
    let b = latin1(message);
    if let Some(langstr) = Scan::new(&b).word(3) {
        let lang = msg_langstr2langtype(&latin1(&langstr)); // Switch langstr to associated langtype
        if msg_checklangtype(lang) == 1 {
            // Verify it's enabled and set it: pc_setaccountreg(sd, add_str(LANGTYPE_VAR), lang) (For login/char)
            use crate::script::vm::{add_str, CodeKey, RegRef, Rt, State};
            let rt = Rt::new(c);
            let mut st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, sd, 0);
            crate::script::reg::set_reg_num(&rt, &mut st, Some(sd), add_str(LANGTYPE_VAR), 0, LANGTYPE_VAR, lang as i64, &RegRef::None);
            // sd->langtype = lang (always 0 here)
            out.say(&format_msg(msg_txt(461), &[&msg_langtype2langstr(lang)])); // Language is now set to %s.
            return 0;
        } else if lang != -1 {
            // defined langage but failed check
            out.say(msg_txt(462)); // This langage is currently disabled.
            return -1;
        }
    }
    // wrong or no entry
    out.say(msg_txt(460)); // Please enter a valid language (usage: @langtype <language>).
    out.say(msg_txt(464)); // ---- Available languages:
    let mut i = 0;
    loop {
        let fail = msg_checklangtype(i);
        if fail == -1 {
            break; // out of range
        }
        if fail == 1 {
            out.say(msg_langtype2langstr(i));
        }
        i += 1;
    }
    -1
}

// ---------------------------------------------------------------------------------------------- set

/// ACMD_FUNC(set): @set <variable name{[index]}>{ <value>}: sets (with a value) then shows a player / account / map
/// variable, through the script engine's registries (setd_sub_num / setd_sub_str = set_reg_num / set_reg_str without
/// a script state, then pc_readreg / mapreg_readreg / pc_readaccountreg(2) / pc_readglobalreg by the first letter).
/// rAthena quirks kept: the value is read back from a registry picked by the name's prefix, so a parameter name
/// ("@set Zeny 100") changes the parameter (set_reg_num's C_PARAM branch) but shows the character variable of that
/// name (0); a name longer than 31 characters is cut to 31 for the variable while the message shows it whole.
fn set(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    use crate::script::reg::{self, Store};
    use crate::script::vm::{add_str, is_string_variable, CodeKey, RegRef, Rt, State};
    // (toset = sscanf(message, "%45s %127[^\n]s", reg, val)) < 1
    let b = latin1(message);
    let mut s = Scan::new(&b);
    let regw = s.word(45);
    let val = regw.as_ref().and_then(|_| {
        s.ws();
        s.set(127, b"\n")
    });
    let Some(regw) = regw.filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1367)); // Usage: @set <variable name> <value>
        out.say(msg_txt(1368)); // Usage: ex. "@set PoringCharVar 50"
        out.say(msg_txt(1369)); // Usage: ex. "@set PoringCharVarSTR$ Super Duper String"
        out.say(msg_txt(1370)); // Usage: ex. "@set PoringCharVarSTR$" outputs its value, Super Duper String.
        return -1;
    };
    let toset = 1 + val.is_some() as i32;
    let regb = latin1(&regw);
    // disabled variable types (they require a proper script state to function, so allowing them would crash the server)
    match regb.first() {
        Some(b'.') => {
            out.say(msg_txt(1371)); // NPC variables may not be used with @set.
            return -1;
        }
        Some(b'\'') => {
            out.say(msg_txt(1372)); // Instance variables may not be used with @set.
            return -1;
        }
        _ => {}
    }
    // Check if the user wanted to set an array: sscanf(reg, "%31[^[][%u]", name, &index) < 2 → index 0
    let (name, index) = {
        let mut s2 = Scan::new(&regb);
        match s2.set(31, b"[") {
            // rAthena leaves name[] uninitialised when the name starts with '[' (undefined behaviour); here it is empty:
            // nothing is set and the value shown is 0
            None => (Vec::new(), 0u32),
            Some(n) => {
                let index = if s2.lit(b'[') { super::c_strtoul(&from_latin1(s2.rest())) as u32 } else { 0 };
                (latin1(&n), index)
            }
        }
    };
    let is_str = is_string_variable(&name);
    let mut valb = val.map(|v| latin1(&v)).unwrap_or_default();
    let len = valb.len();
    if len > 1 && valb[0] == b'"' && valb[len - 1] == b'"' {
        // Strip quotes.
        valb.truncate(len - 1);
        valb.remove(0);
    }
    let rt = Rt::new(c);
    let mut st = State::new(CodeKey::Func(String::new()), std::rc::Rc::new(Default::default()), 0, sd, 0);
    // Only set the variable if there is a value for it
    if toset >= 2 && !name.is_empty() {
        let id = add_str(&name);
        if is_str {
            reg::set_reg_str(&rt, &mut st, Some(sd), id, index, &name, &valb, &RegRef::None);
        } else {
            reg::set_reg_num(&rt, &mut st, Some(sd), id, index, &name, c_strtoll(&valb), &RegRef::None);
        }
    }
    // uid = reference_uid(add_str(name), index), read from the registry of reg[0]
    let key = reg::key_of(&name);
    let store = match regb.first() {
        Some(b'@') => Some(Store::CharTmp(sd)),
        Some(b'$') => Some(Store::Map),
        Some(b'#') => Some(if regb.get(1) == Some(&b'#') { Store::GlobalAcc(sd) } else { Store::Acc(sd) }),
        _ => crate::pc::get(c, sd).map(|p| Store::Char(p.char_id)),
    };
    let text = if is_str {
        let value = match (&store, name.is_empty()) {
            (Some(s), false) => reg::store_get_str(&rt, &st, s, &key, index),
            _ => Vec::new(),
        };
        if value.is_empty() {
            format_msg(msg_txt(1375), &[&regw]) // %s is blank.
        } else {
            format_msg(msg_txt(1374), &[&regw, &from_latin1(&value)]) // %s value is now: %s
        }
    } else {
        let value = match (&store, name.is_empty()) {
            (Some(s), false) => reg::store_get_num(&rt, &st, s, &key, index),
            _ => 0,
        };
        format_msg(msg_txt(1373), &[&regw, &value]) // %s value is now: %lld
    };
    out.say(&text);
    0
}

// ---------------------------------------------------------------------------------------------- setbattleflag

/// battle.cpp:8918 battle_set_value(w1, w2): 0 when no battle_data entry has the name (strcmpi), else 1 after storing
/// the value (config_switch; a value outside the entry's range becomes its default, with a warning). The run-time
/// battle_config is battle_config/mod.rs (port wave I).
pub fn battle_set_value(c: &Ctx, flag: &str, value: &str) -> i32 {
    crate::battle_config::battle_set_value(c, flag, value)
}

/// ACMD_FUNC(setbattleflag): @setbattleflag <flag> <value> {<reload>}: the messages follow battle_set_value's result;
/// `reload` runs mob_reload as rAthena does.
fn setbattleflag(c: &Ctx, out: &Out, message: &str) -> i32 {
    // sscanf(message, "%127s %127s %11d", flag, value, &reload) < 2
    let b = latin1(message);
    let mut s = Scan::new(&b);
    let flag = s.word(127);
    let value = flag.as_ref().and_then(|_| s.word(127));
    let reload = value.as_ref().and_then(|_| s.int(11)).map(|v| v as i32).unwrap_or(0);
    let (Some(flag), Some(value)) = (flag, value) else {
        out.say(msg_txt(1231)); // Usage: @setbattleflag <flag> <value> {<reload>}
        return -1;
    };
    if battle_set_value(c, &flag, &value) == 0 {
        out.say(msg_txt(1232)); // Unknown battle_config flag.
        return -1;
    }
    out.say(msg_txt(1233)); // Set battle_config as requested.
    if reload != 0 {
        mob_reload(c);
    }
    0
}

// ---------------------------------------------------------------------------------------------- send

/// clif.hpp MAX_PACKET_DB.
const MAX_PACKET_DB: i32 = 0xCFF;
/// SHRT_MAX.
const SHRT_MAX: i32 = 0x7FFF;

/// What @send does: the packet (if any), the lines shown after it, the command's return value.
#[derive(Debug, PartialEq)]
struct SendOutcome {
    packet: Option<Vec<u8>>,
    lines: Vec<String>,
    ret: i32,
}

/// glibc sscanf `%Wx` (and `%Wlx`): white space skipped, an optional sign, an optional "0x" (its 0 counts as a digit),
/// hex digits; at most `width` characters after the white space; the value converted by strtoul (a minus negates in
/// unsigned arithmetic, ULONG_MAX on overflow). None when no digit was read.
fn scan_hex(p: &[u8], width: usize) -> Option<u64> {
    let mut i = 0;
    while i < p.len() && crate::cstr::is_space(p[i]) {
        i += 1;
    }
    let mut n = 0usize;
    let mut neg = false;
    if n < width && i < p.len() && (p[i] == b'+' || p[i] == b'-') {
        neg = p[i] == b'-';
        i += 1;
        n += 1;
    }
    let mut digits: Vec<u8> = Vec::new();
    if n < width && i < p.len() && p[i] == b'0' {
        digits.push(b'0');
        i += 1;
        n += 1;
        if n < width && i < p.len() && (p[i] == b'x' || p[i] == b'X') {
            i += 1;
            n += 1;
        }
    }
    while n < width && i < p.len() && p[i].is_ascii_hexdigit() {
        digits.push(p[i]);
        i += 1;
        n += 1;
    }
    if digits.is_empty() {
        return None;
    }
    let mut v: u64 = 0;
    for d in digits {
        let x = (d as char).to_digit(16).unwrap_or(0) as u64;
        v = match v.checked_mul(16).and_then(|v| v.checked_add(x)) {
            Some(v) => v,
            None => return Some(u64::MAX),
        };
    }
    Some(if neg { v.wrapping_neg() } else { v })
}

/// @send's GET_VALUE(p, num): `sscanf(p, "x%16lx", &num) < 1 && sscanf(p, "%20ld ", &num) < 1` fails. The pointer
/// does not move (SKIP_VALUE moves it afterwards).
fn get_value(p: &[u8]) -> Option<i64> {
    if p.first() == Some(&b'x') {
        if let Some(v) = scan_hex(&p[1..], 16) {
            return Some(v as i64);
        }
    }
    Scan::new(p).int(20)
}

/// The value of a character in @send's `\x` escape: `ISDIGIT(c) ? c-'0' : TOLOWER(c)-'a'+10` (for any character).
fn hex_digit_value(ch: u8) -> i64 {
    if ch.is_ascii_digit() {
        (ch - b'0') as i64
    } else {
        ch.to_ascii_lowercase() as i64 - b'a' as i64 + 10
    }
}

/// The packet @send builds from `message` (atcommand.cpp ACMD_FUNC(send)), with rAthena's quirks: a B / W / L value
/// is read by GET_VALUE without moving on, so "B 5" writes 5 twice (SKIP_VALUE then finds the 5 again); `\xAB`
/// shifts the first digit by 8 instead of 4, so only the second digit's value reaches the byte; the string loop never
/// checks the packet length (only `len` bytes of a fixed-length packet are sent).
fn send_build(message: &[u8], packet_len: impl Fn(u16) -> Option<i16>) -> SendOutcome {
    let fail = |lines: Vec<String>| SendOutcome { packet: None, lines, ret: -1 };
    // read message type as hex number (without the 0x): sscanf(message, "len %8x") == 1 && (len=1), or "%8x"
    let mut len_query = false;
    let mut ty: Option<i32> = None;
    if message.starts_with(b"len") {
        if let Some(v) = scan_hex(&message[3..], 8) {
            ty = Some(v as u32 as i32);
            len_query = true;
        }
    }
    if ty.is_none() {
        ty = scan_hex(message, 8).map(|v| v as u32 as i32);
    }
    let Some(ty) = ty.filter(|_| !message.is_empty()) else {
        return fail((900..=903).map(|i| msg_txt(i).to_string()).collect());
    };
    if !(ty > 0 && ty < MAX_PACKET_DB) {
        return fail(vec![msg_txt(259).to_string()]); // Invalid packet
    }
    let db_len = packet_len(ty as u16).unwrap_or(0) as i32;
    if len_query {
        // show packet length
        return SendOutcome { packet: None, lines: vec![format_msg(msg_txt(904), &[&format!("{ty:x}"), &db_len])], ret: 0 }; // Packet 0x%x length: %d
    }
    let mut len = db_len;
    let mut off: i32 = 2;
    if len == 0 {
        // unknown packet - ERROR
        return fail(vec![format_msg(msg_txt(905), &[&format!("{ty:x}")])]); // Unknown packet: 0x%x
    } else if len == -1 {
        // dynamic packet
        len = SHRT_MAX - 4; // maximum length
        off = 4;
    }
    let mut buf: Vec<u8> = vec![0; off as usize];
    buf[0..2].copy_from_slice(&(ty as u16).to_le_bytes()); // WFIFOW(fd,0)=TOW(type)
    let put = |buf: &mut Vec<u8>, at: i32, bytes: &[u8]| {
        let at = at.max(0) as usize;
        if buf.len() < at + bytes.len() {
            buf.resize(at + bytes.len(), 0);
        }
        buf[at..at + bytes.len()].copy_from_slice(bytes);
    };
    // PARSE_ERROR(error, p) and CHECK_EOS(p)
    let parse_error = |error: &str, rest: &[u8]| fail(vec![error.to_string(), format!(">{}", from_latin1(rest))]);
    let eos = || fail(vec!["Unexpected end of string".to_string()]);
    let m = message;
    let mut p = 0usize;
    // SKIP_VALUE(p)
    let skip_value = |p: &mut usize| {
        while *p < m.len() && !crate::cstr::is_space(m[*p]) {
            *p += 1; // non-space
        }
        while *p < m.len() && crate::cstr::is_space(m[*p]) {
            *p += 1; // space
        }
    };
    // parse packet contents
    skip_value(&mut p);
    while p < m.len() && off < len {
        let ch = m[p];
        if ch.is_ascii_digit() || ch == b'-' || ch == b'+' {
            // default (byte)
            let Some(num) = get_value(&m[p..]) else { return parse_error("Invalid number in:", &m[p..]) };
            put(&mut buf, off, &[num as u8]);
            off += 1;
        } else if ch.to_ascii_uppercase() == b'B' {
            // byte
            p += 1;
            let Some(num) = get_value(&m[p..]) else { return parse_error("Invalid number in:", &m[p..]) };
            put(&mut buf, off, &[num as u8]);
            off += 1;
        } else if ch.to_ascii_uppercase() == b'W' {
            // word (2 bytes)
            p += 1;
            let Some(num) = get_value(&m[p..]) else { return parse_error("Invalid number in:", &m[p..]) };
            put(&mut buf, off, &(num as u16).to_le_bytes());
            off += 2;
        } else if ch.to_ascii_uppercase() == b'L' {
            // long word (4 bytes)
            p += 1;
            let Some(num) = get_value(&m[p..]) else { return parse_error("Invalid number in:", &m[p..]) };
            put(&mut buf, off, &(num as u32).to_le_bytes());
            off += 4;
        } else if ch.to_ascii_uppercase() == b'S' {
            // string - escapes are valid; get string length - num <= 0 means not fixed length (default)
            p += 1;
            let num: i64 = if m.get(p) == Some(&b'"') {
                0
            } else {
                let Some(num) = get_value(&m[p..]) else { return parse_error("Invalid number in:", &m[p..]) };
                while m.get(p) != Some(&b'"') {
                    // find start of string
                    if p >= m.len() || crate::cstr::is_space(m[p]) {
                        return parse_error(msg_txt(906), &m[p.min(m.len())..]); // Not a string:
                    }
                    p += 1;
                }
                num
            };
            // parse string
            p += 1;
            if p >= m.len() {
                return eos();
            }
            // end=(num<=0? 0: min(off+((int32)num),len))
            let end: i32 = if num <= 0 { 0 } else { off.wrapping_add(num as i32).min(len) };
            while m[p] != b'"' && (off < end || end == 0) {
                let v: i64;
                if m[p] == b'\\' {
                    p += 1;
                    if p >= m.len() {
                        return eos();
                    }
                    match m[p] {
                        b'a' => v = 0x07, // Bell
                        b'b' => v = 0x08, // Backspace
                        b't' => v = 0x09, // Horizontal tab
                        b'n' => v = 0x0A, // Line feed
                        b'v' => v = 0x0B, // Vertical tab
                        b'f' => v = 0x0C, // Form feed
                        b'r' => v = 0x0D, // Carriage return
                        b'e' => v = 0x1B, // Escape
                        b'x' => {
                            // Hexadecimal
                            p += 1;
                            if p >= m.len() {
                                return eos();
                            }
                            if !m[p].is_ascii_hexdigit() {
                                return parse_error(msg_txt(907), &m[p..]); // Not a hexadecimal digit:
                            }
                            let mut n = hex_digit_value(m[p]);
                            // `if (ISXDIGIT(*message))` tests the same character again: always true
                            p += 1;
                            if p >= m.len() {
                                return eos();
                            }
                            n <<= 8;
                            n += hex_digit_value(m[p]);
                            put(&mut buf, off, &[n as u8]);
                            p += 1;
                            if p >= m.len() {
                                return eos();
                            }
                            off += 1; // `continue`: the for loop's ++off
                            continue;
                        }
                        d @ b'0'..=b'7' => {
                            // Octal
                            let mut n = (d - b'0') as i64; // 1st octal digit
                            p += 1;
                            if p >= m.len() {
                                return eos();
                            }
                            if m[p].is_ascii_digit() && m[p] < b'8' {
                                n = (n << 3) + (m[p] - b'0') as i64; // 2nd octal digit
                                p += 1;
                                if p >= m.len() {
                                    return eos();
                                }
                                if m[p].is_ascii_digit() && m[p] < b'8' {
                                    n = (n << 3) + (m[p] - b'0') as i64; // 3rd octal digit
                                    p += 1;
                                    if p >= m.len() {
                                        return eos();
                                    }
                                }
                            }
                            put(&mut buf, off, &[n as u8]);
                            off += 1; // `continue`: the for loop's ++off
                            continue;
                        }
                        other => v = other as i64,
                    }
                } else {
                    v = m[p] as i64;
                }
                put(&mut buf, off, &[v as u8]);
                p += 1;
                if p >= m.len() {
                    return eos();
                }
                off += 1;
            }
            while m[p] != b'"' {
                // ignore extra characters
                p += 1;
                if p >= m.len() {
                    return eos();
                }
            }
            // terminate the string
            if off < end {
                // fill the rest with 0's
                put(&mut buf, off, &vec![0u8; (end - off) as usize]);
                off = end;
            }
        } else {
            // unknown
            return parse_error(msg_txt(908), &m[p..]); // Unknown type of value in:
        }
        skip_value(&mut p);
    }
    let packet = if db_len == -1 {
        // send dynamic packet: WFIFOW(fd,2)=TOW(off); WFIFOSET(fd,off)
        put(&mut buf, 2, &(off as u16).to_le_bytes());
        buf.resize(off.max(0) as usize, 0);
        buf
    } else {
        // send static packet: the rest zeroed, WFIFOSET(fd,len)
        buf.resize(len.max(0) as usize, 0);
        buf
    };
    SendOutcome { packet: Some(packet), lines: vec![format_msg(msg_txt(258), &[&format!("{ty:x}"), &ty])], ret: 0 } // Sent packet 0x%x (%d)
}

/// ACMD_FUNC(send): @send len <packet hex number> / @send <packet hex number> {<value>}* (packet_db lengths =
/// net::packets::packet_len). The packet goes to the invoker's own connection (WFIFOSET(fd, …)).
fn send(c: &Ctx, out: &Out, message: &str) -> i32 {
    let r = send_build(&latin1(message), crate::net::packets::packet_len);
    if let (Some(pkt), Some(u)) = (&r.packet, out.fd) {
        if let Some(id) = crate::pc::identity_of(c, u.id) {
            c.send(id, pkt);
        }
    }
    for l in &r.lines {
        out.say(l);
    }
    r.ret
}

// ---------------------------------------------------------------------------------------------- @reload

/// ACMD_FUNC(reload): @reload <type>: the type is found by a case-insensitive full match, then a case-sensitive partial
/// one (strstr), in the table's order; the group must be allowed the TYPE as a command (pc_can_use_command(sd,
/// "itemdb", …): only groups with all_commands pass, rAthena's quirk kept). The type's function runs with the type as
/// its command and no parameters.
fn reload(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    const TYPES: [&str; 17] = [
        "achievementdb", "atcommand", "attendancedb", "barterdb", "battleconf", "cashdb", "instancedb", "itemdb", "logconf", "mobdb", "motd",
        "msgconf", "pcdb", "script", "skilldb", "statusdb", "questdb",
    ];
    let Some(p) = crate::pc::get(c, sd) else { return -1 };
    let help = || {
        if let Some(text) = crate::data::atcommands().help_string(command) {
            out.say(text);
        }
    };
    if message.is_empty() {
        help();
        return -1;
    }
    let m = latin1(message);
    // Case insensitive full string search, then case sensitive partial string search
    let found = TYPES
        .iter()
        .find(|t| t.as_bytes().eq_ignore_ascii_case(&m))
        .or_else(|| TYPES.iter().find(|t| t.as_bytes().windows(m.len()).any(|w| w == m.as_slice())));
    let Some(&ty) = found else {
        // No valid type specified
        help();
        return -1;
    };
    if !crate::rules::can_use_command(p.group_id, ty, false) {
        return -1;
    }
    match ty {
        "achievementdb" => reloadachievementdb(out),
        "atcommand" => reloadatcommand(c, out),
        "attendancedb" => reloadattendancedb(out),
        "barterdb" => reloadbarterdb(c, out),
        "battleconf" => reloadbattleconf(c, out),
        "cashdb" => reloadcashdb(c, out),
        // atcommand_reloadinstancedb (atcommand_instance.rs: the database is embedded, instance_db.reload() succeeds)
        "instancedb" => {
            out.say(msg_txt(516)); // Instance database has been reloaded.
            0
        }
        "itemdb" => reloaditemdb(c, out),
        "logconf" => reloadlogconf(out),
        "mobdb" => reloadmobdb(c, out),
        "motd" => reloadmotd(out),
        "msgconf" => reloadmsgconf(out),
        "pcdb" => reloadpcdb(out),
        "script" => reloadscript(c, out),
        "skilldb" => reloadskilldb(c, out),
        "statusdb" => reloadstatusdb(out),
        _ => reloadquestdb(c, out),
    }
}

// ---------------------------------------------------------------------------------------------- item db

/// pc.hpp ITMCHK_ALL.
const ITMCHK_ALL: u8 = crate::inventory::ITMCHK_INVENTORY | crate::inventory::ITMCHK_CART | crate::inventory::ITMCHK_STORAGE;

/// itemdb.cpp itemdb_reload.
///
/// Re-reading: do_final_itemdb + itemdb_read give back the embedded item_db, combos, item groups, random options,
/// laphine / reform / enchant / package databases. What they forget is ported: the setiteminfo / setitemscript edits
/// (itemui/itemdb_rt.rs), the `autoequip` changes (inventory.rs itemdb_runtime), the SHAREDPOOL counters of the item
/// groups (itemgroup.rs item_group_pool), and every item's mob[] search list, which starts empty and is refilled by
/// mob_reload_itemmob_data. NOT ported: item_data::maxchance also restarts at 0 for every item (item_data is
/// value-initialised), so rAthena's @iteminfo says "Monsters don't drop this item." for everything until
/// @reloadmobdb; the module computes maxchance from static data (npc::item_maxchance) and has no run-time value to
/// reset. The roulette table (conf/battle/feature.conf:73 `feature.roulette: on`): do_final_itemdb's
/// itemdb_roulette_free and itemdb_read's itemdb_parse_roulette_db re-read `rd` from the `db_roulette` rows, which
/// give the embedded table again (db/items/roulette.rs; nothing changes it at run time); the players' sd->roulette is
/// not touched.
fn itemdb_reload(c: &Ctx) {
    use crate::inventory::itemdb_runtime as _;
    use crate::itemgroup::item_group_pool as _;
    use crate::itemui::itemdb_rt::{itemdb_edit as _, itemdb_edit_state as _, ItemdbEditState};
    use crate::npc::world_state as _;
    // do_final_itemdb / itemdb_read: the run-time edits of this world's item_db are gone
    let world = c.db().world_state().key().find(0).map(|w| w.built_at_tick).unwrap_or(0);
    let edits: Vec<u64> = c.db().itemdb_edit().iter().filter(|e| e.world == world).map(|e| e.id).collect();
    if !edits.is_empty() {
        for id in edits {
            c.db().itemdb_edit().id().delete(id);
        }
        let mut s = c.db().itemdb_edit_state().key().find(0).unwrap_or(ItemdbEditState { key: 0, version: 0 });
        s.version += 1;
        if c.db().itemdb_edit_state().key().find(0).is_some() {
            c.db().itemdb_edit_state().key().update(s);
        } else {
            c.db().itemdb_edit_state().insert(s);
        }
        crate::itemui::itemdb_rt::sync(c);
    }
    for id in c.db().itemdb_runtime().iter().map(|r| r.nameid).collect::<Vec<_>>() {
        c.db().itemdb_runtime().nameid().delete(id);
    }
    for k in c.db().item_group_pool().iter().map(|r| r.key).collect::<Vec<_>>() {
        c.db().item_group_pool().key().delete(k);
    }
    // do_final_itemdb: `if (battle_config.feature_roulette) itemdb_roulette_free();`, itemdb_read's last step:
    // `if (battle_config.feature_roulette) itemdb_parse_roulette_db();` — the same rows, so the same embedded `rd`
    // (db/items/roulette.rs; no SQL table here that could have changed in between)
    cashshop_reloaddb(c);
    // mob_reload_itemmob_data: item_data::mob[] of the new item_db is empty, then filled
    item_mob_insert_drops(c, true, false);
    // readjust itemdb pointer cache for each player (mapit_geteachpc)
    for aid in pc_db_order(c) {
        if let Some(mut s) = crate::inventory::st(c, aid) {
            s.item_delay = vec![crate::inventory::ItemDelay::default(); crate::inventory::MAX_ITEMDELAYS]; // reset item delays
            s.combos.clear(); // clear combo bonuses
            crate::inventory::put_st(c, &s);
        }
        // pc_setinventorydata: inventory_data[] is itemdb_search of each slot at every use here (inventory.rs)
        crate::inventory::pc_check_available_item(c, aid, ITMCHK_ALL); // Check for invalid(ated) items.
        crate::pc_equip::pc_load_combo(c, aid); // Check to see if new combos are available
        crate::status::calc_pc(c, aid, crate::status::SCO_FORCE);
    }
}

/// item_data::mob[] as the embedded data has it (mob_drop_ratio_adjust at start-up).
fn static_item_mob(nameid: u32) -> [(i32, i32); crate::mob::rtdb::MAX_SEARCH] {
    let mut out = [(0, 0); crate::mob::rtdb::MAX_SEARCH];
    if let Some(info) = crate::db::mobs::mob_item_drop_info().find(nameid) {
        for (k, &slot) in info.mob.iter().take(crate::mob::rtdb::MAX_SEARCH).enumerate() {
            out[k] = slot;
        }
    }
    out
}

/// Every monster's drops (clones are never in the static mob_db; rAthena skips them) go into their item's mob[] list,
/// highest chance first, in mob_db's order: mob.cpp mob_reload_itemmob_data (`from_empty`: after itemdb_reload's new
/// item_db; treasure chests included) or the item part of mob_drop_ratio_adjust in mob_reload (`skip_treasure`, and
/// the lists are not cleared, so a monster can end up twice in one, as in rAthena). The rate is the adjusted one (the
/// database keeps the drops after mob_drop_ratio_adjust; a reload adjusts the same file rates again). The result is
/// kept as `mob::rtdb` item_mob_rt rows (a row only where it differs from the embedded list).
fn item_mob_insert_drops(c: &Ctx, from_empty: bool, skip_treasure: bool) {
    use crate::mob::rtdb::{self, item_mob_rt as _, ItemMobRt, MAX_SEARCH};
    use std::collections::HashMap;
    let mut work: HashMap<u32, [(i32, i32); MAX_SEARCH]> = HashMap::new();
    let mut order: Vec<u32> = Vec::new();
    for mob in mob_db_iter(c) {
        if skip_treasure && mob.race2.contains(&crate::mob::RC2_TREASURE) {
            continue; // "calculate and store Max available drop chance of the item but skip treasure chests"
        }
        for d in rtdb::dropitem(c, mob.id) {
            if d.nameid == 0 || (skip_treasure && d.rate == 0) {
                continue;
            }
            // itemdb_search: an unknown id is the dummy item
            let key = if crate::db::items::itemdb_exists(d.nameid).is_some() { d.nameid } else { crate::db::items::ITEMID_DUMMY };
            let arr = work.entry(key).or_insert_with(|| {
                order.push(key);
                if from_empty {
                    [(0, 0); MAX_SEARCH]
                } else {
                    rtdb::item_mob(c, key)
                }
            });
            let Some(k) = (0..MAX_SEARCH).find(|&k| (arr[k].0 as i64) <= d.rate as i64) else { continue };
            if arr[k].1 != mob.id as i32 {
                for j in (k + 1..MAX_SEARCH).rev() {
                    arr[j] = arr[j - 1];
                }
            }
            arr[k] = (d.rate as i32, mob.id as i32);
        }
    }
    if from_empty {
        // every other item's list is empty now: the items of the embedded lists and of the existing rows
        let mut others: Vec<u32> = c.db().item_mob_rt().iter().map(|r| r.nameid).collect();
        for mob in crate::db::mobs::mob_db().iter() {
            others.extend(mob.dropitem.iter().map(|d| d.nameid));
        }
        others.sort_unstable();
        others.dedup();
        for nameid in others {
            if !work.contains_key(&nameid) {
                work.insert(nameid, [(0, 0); MAX_SEARCH]);
                order.push(nameid);
            }
        }
    }
    for nameid in order {
        let arr = work[&nameid];
        let existing = c.db().item_mob_rt().nameid().find(nameid);
        if arr == static_item_mob(nameid) {
            if existing.is_some() {
                c.db().item_mob_rt().nameid().delete(nameid);
            }
            continue;
        }
        let row = ItemMobRt { nameid, chance: arr.iter().map(|s| s.0).collect(), mob_id: arr.iter().map(|s| s.1).collect() };
        if existing.is_some() {
            c.db().item_mob_rt().nameid().update(row);
        } else {
            c.db().item_mob_rt().insert(row);
        }
    }
}

/// ACMD_FUNC(reloaditemdb).
fn reloaditemdb(c: &Ctx, out: &Out) -> i32 {
    itemdb_reload(c);
    out.say(msg_txt(97)); // Item database has been reloaded.
    0
}

/// cashshop.cpp cashshop_reloaddb = do_final_cashshop + do_init_cashshop.
///
/// Re-reading: cash_shop_db is the embedded cash shop. The sales (cashshop.rs `cash_sale`: rAthena's `sales` SQL table
/// and sale_items at once) go through rAthena's cycle: every sale's timers are deleted and the list emptied;
/// sale_read_db_sql loads the rows with `end > now()` in table order (MyISAM: insertion order = `order`); the rows
/// with `end < now` are deleted; each loaded sale gets a start timer when it starts later, else sale_start_timer runs
/// at once (clif_sale_start + clif_sale_amount to every client, a new end timer). A row whose end is exactly now is
/// neither loaded nor deleted in rAthena (it stays in SQL only); here it keeps its row without timers.
pub(crate) fn cashshop_reloaddb(c: &Ctx) {
    use crate::cashshop::{cash_sale as _, CashSale};
    use crate::db::items::cash;
    let now = c.tick().div_euclid(1000);
    let mut rows: Vec<CashSale> = c.db().cash_sale().iter().collect();
    rows.sort_by_key(|s| s.order);
    // do_final_cashshop: the timers of every sale
    for s in &mut rows {
        if s.timer_start == 0 && s.timer_end == 0 {
            continue;
        }
        if s.timer_start != 0 {
            crate::timer::delete(c, s.timer_start);
            s.timer_start = 0;
        }
        if s.timer_end != 0 {
            crate::timer::delete(c, s.timer_end);
            s.timer_end = 0;
        }
        c.db().cash_sale().nameid().update(s.clone());
    }
    // sale_read_db_sql, with sale_parse_dbrow's checks (the item exists and is in the sale tab, start < end)
    let loaded: Vec<CashSale> = rows
        .iter()
        .filter(|s| s.end > now)
        .filter(|s| crate::db::items::itemdb_exists(s.nameid).is_some() && cash::find_item_in_tab(cash::CASHSHOP_TAB_SALE, s.nameid).is_some() && s.start < s.end)
        .cloned()
        .collect();
    // Clean outdated sales: DELETE FROM sales WHERE end < now
    for s in rows.iter().filter(|s| s.end < now) {
        c.db().cash_sale().nameid().delete(s.nameid);
    }
    // Init next sale start, if there is any
    for s in loaded {
        if s.start > now {
            // add_timer(gettick() + (uint32)(start - time(nullptr)) * 1000, …): uint32 arithmetic
            let due = c.tick() + ((s.start - now) as u32).wrapping_mul(1000) as i64;
            let tid = crate::timer::add(c, due, crate::timer::Func::SaleStartTimer, s.nameid, 0);
            if let Some(mut r) = c.db().cash_sale().nameid().find(s.nameid) {
                r.timer_start = tid;
                c.db().cash_sale().nameid().update(r);
            }
        } else {
            crate::cashshop::sale_start_timer(c, s.nameid);
        }
    }
}

/// ACMD_FUNC(reloadcashdb).
fn reloadcashdb(c: &Ctx, out: &Out) -> i32 {
    cashshop_reloaddb(c);
    out.say(msg_txt(832)); // Cash shop database has been reloaded.
    0
}

// ------------------------------------------------------------------------ iteration order after a reload

/// The iteration order of mob_db and skill_db once they have been reloaded (tools/datagen_mods/zz_reload_orders.py).
/// Both are std::unordered_maps (common/database.hpp TypesafeYamlDatabase::data); a reload clear()s them — libstdc++
/// keeps the bucket count — and loads the same keys again, so without the rehashes of the first load the order is a
/// different one, the same after every later reload.
#[derive(serde::Deserialize)]
pub struct ReloadOrders {
    pub mob_db: Vec<u32>,
    pub skill_db: Vec<u16>,
    /// barter_db (npc.cpp BarterDatabase, keyed by name): the barter names (npc_load.rs, @reloadbarterdb / npc_reload).
    #[serde(default)]
    pub barter_db: Vec<String>,
}

crate::dataset!(reload_orders, "reload_orders", ReloadOrders);

/// A database that has been cleared and loaded again since the server started ("mob_db", "skill_db"): its iteration
/// follows `reload_orders`. (A restart loads everything once, as the first start does.)
#[spacetimedb::table(accessor = db_reloaded)]
#[derive(Clone, Debug)]
pub struct DbReloaded {
    #[primary_key]
    pub db: String,
}

fn set_reloaded(c: &Ctx, db: &str) {
    if c.db().db_reloaded().db().find(db.to_string()).is_none() {
        c.db().db_reloaded().insert(DbReloaded { db: db.to_string() });
    }
}

fn is_reloaded(c: &Ctx, db: &str) -> bool {
    c.db().db_reloaded().db().find(db.to_string()).is_some()
}

/// `for (auto& pair : mob_db)` as rAthena iterates it now: the first load's order, or the reload order after
/// @reloadmobdb (mob_reload's mob_db.clear() + load).
pub(crate) fn mob_db_iter(c: &Ctx) -> Box<dyn Iterator<Item = &'static crate::db::mobs::MobDb>> {
    let db = crate::db::mobs::mob_db();
    if is_reloaded(c, "mob_db") {
        Box::new(reload_orders().mob_db.iter().filter_map(move |&id| db.find(id)))
    } else {
        Box::new(db.iter())
    }
}

/// `for (auto& pair : skill_db)` as rAthena iterates it now (the reload order after skill_reload's skill_db.clear()).
pub(crate) fn skill_db_iter(c: &Ctx) -> Box<dyn Iterator<Item = &'static crate::db::skills::SkillDb>> {
    let db = crate::db::skills::skill_db();
    if is_reloaded(c, "skill_db") {
        Box::new(reload_orders().skill_db.iter().filter_map(move |&id| db.find(id)))
    } else {
        Box::new(db.iter())
    }
}

/// mob.cpp mobdb_searchname (full comparison): the first match in mob_db's current iteration order, 0 when none.
pub(crate) fn mobdb_searchname(c: &Ctx, name: &str) -> u16 {
    let has_spawn = crate::mob::spawn::mob_has_spawn_fn(c);
    mob_db_iter(c)
        .map(|m| m.id as u16)
        .find(|&id| crate::db::mobs::mobdb_searchname_sub(id, name, true, &has_spawn))
        .unwrap_or(0)
}

// ---------------------------------------------------------------------------------------------- mob db

/// mob.cpp mob_reload (do_final_mob(true), mob_db_load(true), map_foreachmob(mob_reload_sub),
/// map_foreachnpc(mob_reload_sub_npc)).
///
/// Re-reading: mob_db, mob_chat_db, mob_skill_db, the drop ratio / summon / map drop databases are embedded. What the
/// re-read forgets is ported: clones (mob_clone_spawn's entries) and the addmonsterdrop / delmonsterdrop edits (both
/// `mob::rtdb` mob_db_rt rows) are gone, and mob_drop_ratio_adjust inserts every drop into item_data::mob[] again (the
/// item lists are not cleared). Its maxchance updates change nothing here: they compare the same rates with the values
/// computed at start-up (see itemdb_reload for the maxchance the module cannot reset).
///
/// The monsters' EXP is rated again with base_exp_rate / job_exp_rate / mvp_exp_rate as they are now (mob.cpp:5077-5095;
/// battle_config/mod.rs mob_load, mob/rtdb.rs). The drop rates are still the ones datagen baked into the data.
pub(crate) fn mob_reload(c: &Ctx) {
    use crate::mob::mob_data as _;
    use crate::mob::rtdb::mob_db_rt as _;
    use crate::npc::npc as _;
    // do_final_mob(true) + mob_db_load(true)
    crate::battle_config::mob_load(c);
    for id in c.db().mob_db_rt().iter().map(|r| r.mob_id).collect::<Vec<_>>() {
        c.db().mob_db_rt().mob_id().delete(id);
    }
    // mob_db.clear() kept the unordered_map's buckets: the load that follows gives the reload order
    set_reloaded(c, "mob_db");
    // mob_drop_ratio_adjust's item_data part
    item_mob_insert_drops(c, false, true);
    // map_foreachmob(mob_reload_sub): mobid_db order
    let mut mobs: Vec<u32> = c.db().mob_data().iter().map(|m| m.id).collect();
    mobs.sort_by_key(|&id| crate::party::idb_order(id));
    for id in mobs {
        mob_reload_sub(c, id);
    }
    // map_foreachnpc(mob_reload_sub_npc): id_db order
    let mut npcs: Vec<u32> = c.db().npc().iter().map(|n| n.id).collect();
    npcs.sort_by_key(|&id| crate::party::idb_order(id));
    for id in npcs {
        mob_reload_sub_npc(c, id);
    }
}

/// mob.cpp mob_reload_sub: slaves are removed from the map (not freed: rAthena leaves them in memory off the map);
/// a monster whose database entry is gone (a clone) likewise; the others get status_calc_mob and, unless their view
/// data is their own copy, the database's view data and, when on a map, a vanish + spawn to the area.
fn mob_reload_sub(c: &Ctx, id: u32) {
    use crate::unit::CLR_OUTSIGHT;
    let Some(md) = crate::mob::get(c, id) else { return };
    // Slaves have to be killed
    if md.master_id != 0 {
        crate::mob::unit::unit_remove_map(c, id, CLR_OUTSIGHT);
        return;
    }
    // Relink the mob to the new database entry
    if crate::mob::db(c, &md).is_none() {
        log::warn!("mob_reload_sub: Monster {} (ID: {}) does not exist anymore.", md.name, md.mob_id);
        if crate::unit::get(c, id).map(|u| u.on_map).unwrap_or(false) {
            log::debug!("mob_reload_sub: The monster was removed from its map.");
        }
        crate::mob::unit::unit_remove_map(c, id, CLR_OUTSIGHT);
        return;
    }
    // Recalculate the monster status based on the new data
    crate::mob::status::status_calc_mob(c, id, crate::status::SCO_NONE);
    let Some(md) = crate::mob::get(c, id) else { return };
    // If the view data was not overwritten manually
    if !md.vd_changed {
        let Some(mut u) = crate::unit::get(c, id) else { return };
        // Get the new view data from the mob database (md->vd = mob_get_viewdata(md->mob_id): the entry was just found)
        if let Some(vd) = crate::mob::rtdb::mob_get_viewdata(c, md.mob_id) {
            u.vd = crate::mob::status::view_from_db(vd);
            crate::unit::save(c, &u);
        }
        // If they are spawned right now: respawn them on the client side (if their view id changed)
        if u.on_map {
            crate::clif::clearunit_area(c, &u, CLR_OUTSIGHT);
            crate::clif::spawn(c, &u, false);
        }
    }
}

/// mob.cpp mob_reload_sub_npc: an NPC with a monster sprite takes that monster's view data again (memcpy of the whole
/// view_data) and, when on a map, unit_refresh.
fn mob_reload_sub_npc(c: &Ctx, id: u32) {
    use crate::npc::npc as _;
    let Some(nd) = c.db().npc().id().find(id) else { return };
    // If the view data points to a mob
    if crate::db::mobs::mobdb_checkid(nd.class) == 0 {
        return;
    }
    let Some(mut u) = crate::unit::get(c, id) else { return };
    if let Some(vd) = crate::mob::rtdb::mob_get_viewdata(c, nd.class) {
        u.vd = crate::mob::status::view_from_db(vd);
        crate::unit::save(c, &u);
    }
    if u.on_map {
        crate::unit_npc::unit_refresh(c, &u, false); // Respawn all NPCs on client side
    }
}

/// ACMD_FUNC(reloadmobdb).
fn reloadmobdb(c: &Ctx, out: &Out) -> i32 {
    mob_reload(c);
    // pet_db.reload(): PetDatabase::reload → map_foreachpc(pet_reload_sub) (pet bonuses cleared, the support script
    // run again, status_calc_pc and status_calc_pet): crate::pet::pet_reload(c) (pet agent, branch pet: not in this
    // worktree, wired at integration)
    // hom_reload() (atcommand.cpp:4373; homunculus.rs: the embedded databases are the loaded ones)
    crate::homunculus::hom_reload(c);
    // mercenary_db.reload(), elemental_db.reload() (atcommand.cpp:4374-4375): re-reads only (no player or unit is
    // touched); the module's mercenary / elemental databases are embedded (db/misc/mercenary.rs, elemental.rs), so a
    // re-read changes nothing
    out.say(msg_txt(98)); // Monster database has been reloaded.
    0
}

// ---------------------------------------------------------------------------------------------- skill db

/// skill.cpp skill_reload. Re-reading: skill_db and its satellite databases are embedded. Then, for every player,
/// pc_validate_skill and clif_skillinfoblock ("so that if any skill modes were changed they're properly updated").
fn skill_reload(c: &Ctx) {
    // skill_db.clear() kept the unordered_map's buckets: skill_readdb's load gives the reload order
    set_reloaded(c, "skill_db");
    for aid in pc_db_order(c) {
        pc_validate_skill_online(c, aid);
        crate::clif_skill::clif_skillinfoblock(c, aid);
    }
}

/// pc.cpp pc_validate_skill on a character in the world (skill_reload): every slot of sd->status.skill[] is cleared
/// and only the slots with an id and a level come back (at skill_get_index of their id). rAthena quirks kept: the
/// level-0 slots of the current tree (skills the player could learn) lose their id, so the skill list sent next lacks
/// them until the tree is calculated again; a learned skill the current tree hides (id 0, level > 0) is dropped for
/// good. (skilltree.rs pc_validate_skill is the login version, which reloads the character's rows.)
fn pc_validate_skill_online(c: &Ctx, aid: u32) {
    let Some(mut s) = crate::skilltree::Skills::load(c, aid) else { return };
    let tmp = s.snapshot();
    s.slots.clear();
    for (_, sl) in tmp {
        if sl.id == 0 || sl.lv == 0 {
            continue;
        }
        let idx = crate::db::skills::skill_db().get_index(sl.id, true);
        if idx != 0 {
            s.put(idx, sl);
        } else {
            log::warn!("pc_validate_skill: Removing invalid skill '{}' from player (AID={aid} CID={}).", sl.id, s.char_id);
        }
    }
    s.save(c);
}

/// ACMD_FUNC(reloadskilldb).
fn reloadskilldb(c: &Ctx, out: &Out) -> i32 {
    skill_reload(c);
    // homunculus_db.reload() (atcommand.cpp:4385): a re-read of db/homunculus_db.yml only (no homunculus is touched);
    // the module's homunculus database is embedded (db/misc/homunculus.rs): nothing to do
    out.say(msg_txt(99)); // Skill database has been reloaded.
    0
}

// ---------------------------------------------------------------------------------------------- scripts

/// ACMD_FUNC(reloadscript): every player's dialog is closed (pc_close_npc, the cutin cleared, the script block
/// actions lifted) and its battleground queue left (bg_queue_leave), every queue's teams kicked and cleared, then
/// map_reloadnpc(true), script_reload and npc_reload (npc_load.rs: the world's NPCs, monsters, map flags, functions and
/// events made again from the start-up files, OnAgitInit / 2 / 3, OnInit, the instances' NPCs, OnInterIfInit).
fn reloadscript(c: &Ctx, out: &Out) -> i32 {
    //atcommand_broadcast( fd, sd, "@broadcast", "Server is reloading scripts..." );
    //atcommand_broadcast( fd, sd, "@broadcast", "You will feel a bit of lag at this point !" );
    for aid in pc_db_order(c) {
        crate::script::npc_run::pc_close_npc(c, aid, 1);
        if let Some(u) = crate::unit::get(c, aid) {
            crate::clif::cutin(c, &u, b"", 255);
        }
        if let Some(mut p) = crate::pc::get(c, aid) {
            p.block_action &= !(PCBLOCK_ALL ^ crate::pc::PCBLOCK_IMMUNE);
            crate::pc::put(c, &p);
        }
        crate::battleground::bg_queue_leave(c, aid, true);
    }
    // every battleground queue: bg_team_leave of the team A and B members, bg_queue_clear(bg, true)
    crate::battleground::reloadscript_queues(c);
    // flush_fifos(): the reducer's packets leave at its end
    crate::npc_load::map_reloadnpc(c, true); // reload config files seeking for npcs
    crate::npc_load::script_reload(c);
    crate::npc_load::npc_reload(c);
    out.say(msg_txt(100)); // Scripts have been reloaded.
    0
}

/// script.hpp PCBLOCK_ALL.
const PCBLOCK_ALL: u32 = 0x7FF;

// ---------------------------------------------------------------------------------------------- the other reloads

/// ACMD_FUNC(reloadatcommand): atcommand_doload (the command table and conf/atcommands.yml: embedded) and
/// pc_groups_reload (conf/groups.yml: embedded), which runs pc_group_pc_load for every player: the sessions'
/// permission changes (@addperm / @rmvperm, permission_add / permission_remove) are gone.
fn reloadatcommand(c: &Ctx, out: &Out) -> i32 {
    for aid in pc_db_order(c) {
        pc_group_pc_load(c, aid);
    }
    out.say(msg_txt(254)); // GM command configuration has been reloaded.
    0
}

/// ACMD_FUNC(reloadbattleconf) (atcommand.cpp:4436-4485): battle_config_read(BATTLE_CONF_FILENAME), so every member
/// gets its conf value again (battle_set_defaults, conf/battle/*.conf, the import file, battle_adjust_conf) and what
/// battle_set_value changed is gone; then mob_reload when a drop or EXP rate is not what it was before the read
/// (atcommand.cpp:4443-4481).
fn reloadbattleconf(c: &Ctx, out: &Out) -> i32 {
    if crate::battle_config::battle_config_read(c) {
        // Exp or Drop rates changed.
        mob_reload(c); // Needed as well so rate changes take effect.
    }
    out.say(msg_txt(255)); // Battle configuration has been reloaded.
    0
}

/// ACMD_FUNC(reloadstatusdb): status_readdb(true) (status_disabled, size_fix, refine, status, enchantgrade,
/// attr_fix): data only, embedded.
fn reloadstatusdb(out: &Out) -> i32 {
    out.say(msg_txt(256)); // Status database has been reloaded.
    0
}

/// ACMD_FUNC(reloadpcdb): pc_readdb (penalty, job, job_noenter_map, skill_tree, statpoint databases): data only,
/// embedded.
fn reloadpcdb(out: &Out) -> i32 {
    out.say(msg_txt(257)); // Player database has been reloaded.
    0
}

/// ACMD_FUNC(reloadmotd): pc_read_motd (conf/motd.txt: conf.rs motd, embedded).
fn reloadmotd(out: &Out) -> i32 {
    out.say(msg_txt(268)); // Reloaded the Message of the Day.
    0
}

/// ACMD_FUNC(reloadquestdb): QuestDatabase::reload (the embedded quest_db; it succeeds, so the message is sent), which
/// runs map_foreachpc(quest_reload_check_sub).
fn reloadquestdb(c: &Ctx, out: &Out) -> i32 {
    for aid in pc_db_order(c) {
        quest_reload_check_sub(c, aid);
    }
    out.say(msg_txt(1377)); // Quest database has been reloaded.
    0
}

/// quest.cpp quest_reload_check_sub: entries of quests no longer in the database are removed (the client is told
/// about those that were not complete), and avail_quests becomes the index of the first complete entry.
fn quest_reload_check_sub(c: &Ctx, aid: u32) {
    use crate::quest::{pc_quest as _, Q_COMPLETE};
    let Some(mut q) = c.db().pc_quest().account_id().find(aid) else { return };
    let before_len = q.quest_log.len();
    let before_avail = q.avail_quests;
    let mut kept = Vec::with_capacity(before_len);
    for e in q.quest_log.drain(..) {
        if crate::db::misc::quest::quest_search(e.quest_id).is_none() {
            // Remove no longer existing entries, and inform the client if necessary
            if e.state != Q_COMPLETE {
                crate::quest::clif_quest_delete(c, aid, e.quest_id);
            }
            continue;
        }
        kept.push(e);
    }
    q.quest_log = kept;
    q.avail_quests = q.quest_log.iter().position(|e| e.state == Q_COMPLETE).unwrap_or(q.quest_log.len()) as u32;
    if q.quest_log.len() != before_len || q.avail_quests != before_avail {
        c.db().pc_quest().account_id().update(q);
    }
}

/// ACMD_FUNC(reloadmsgconf): map_msg_reload (conf/msg_conf: embedded, English only).
fn reloadmsgconf(out: &Out) -> i32 {
    out.say(msg_txt(463)); // Message configuration has been reloaded.
    0
}

/// ACMD_FUNC(reloadachievementdb): achievement_db_reload (achievement_db / achievement_level_db: embedded; no player
/// is touched).
fn reloadachievementdb(out: &Out) -> i32 {
    out.say(msg_txt(771)); // Achievement database has been reloaded.
    0
}

/// ACMD_FUNC(reloadattendancedb): attendance_db.reload (embedded; no player is touched).
fn reloadattendancedb(out: &Out) -> i32 {
    out.say(msg_txt(795)); // Attendance database has been reloaded.
    0
}

/// ACMD_FUNC(reloadbarterdb): barter_db.reload() (npc_load.rs barter_db_reload): BarterDatabase::clear destroys every
/// barter in the database's iteration order, ~s_npc_barter unloading its NPC (npc_unload(nd, true) with its vanish
/// packet, npc_read_event_script), and loadingFinished creates them again with new NPC ids (clif_spawn on maps with
/// players) in the reload order, reading their stock back (the same values).
fn reloadbarterdb(c: &Ctx, out: &Out) -> i32 {
    crate::npc_load::barter_db_reload(c);
    out.say(msg_txt(830)); // Barter database has been reloaded.
    0
}

/// ACMD_FUNC(reloadlogconf): log_config_read (conf/log_athena.conf is compiled in as log.rs LOG_CONFIG and its import
/// is the `log_conf` table, which log.rs log_config reads at every call: nothing to re-read).
fn reloadlogconf(out: &Out) -> i32 {
    out.say(msg_txt(1536)); // Log configuration has been reloaded.
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The reload orders hold the same keys as the first load, in another order: after @reloadmobdb the oracle's
    /// @disguise poring picks 3384 (MIN_PORING) where a fresh server picks 1973 (E_PORING); @skillid heal lists
    /// AB_HIGHNESSHEAL before NPC_CHEAL after @reloadskilldb, the other way round before.
    #[test]
    fn reload_orders_are_permutations() {
        let (mobs, skills) = (crate::db::mobs::mob_db(), crate::db::skills::skill_db());
        let ro = reload_orders();
        let mut a: Vec<u32> = mobs.iter().map(|m| m.id).collect();
        let mut b = ro.mob_db.clone();
        assert_ne!(a, b);
        a.sort_unstable();
        b.sort_unstable();
        assert_eq!(a, b);
        let mut a: Vec<u16> = skills.iter().map(|s| s.nameid).collect();
        let mut b = ro.skill_db.clone();
        assert_ne!(a, b);
        a.sort_unstable();
        b.sort_unstable();
        assert_eq!(a, b);
        let porings = |v: &[u32]| v.iter().copied().filter(|id| [1002, 1973, 3384].contains(id)).collect::<Vec<_>>();
        assert_eq!(porings(&mobs.iter().map(|m| m.id).collect::<Vec<_>>()), vec![1973, 1002, 3384]);
        assert_eq!(porings(&ro.mob_db), vec![3384, 1973, 1002]);
        let pos = |v: &[u16], id: u16| v.iter().position(|&x| x == id);
        assert!(pos(&ro.skill_db, 2051) < pos(&ro.skill_db, 729));
        let first: Vec<u16> = skills.iter().map(|s| s.nameid).collect();
        assert!(pos(&first, 729) < pos(&first, 2051));
    }

    fn plen(id: u16) -> Option<i16> {
        match id {
            0x0080 => Some(7),
            0x008e => Some(-1),
            0x0002 => Some(3),
            _ => None,
        }
    }

    #[test]
    fn send_length_query_and_errors() {
        let r = send_build(b"len 80", plen);
        assert_eq!(r.ret, 0);
        assert_eq!(r.packet, None);
        assert_eq!(r.lines, vec!["Packet 0x80 length: 7".to_string()]);
        let r = send_build(b"", plen);
        assert_eq!(r.ret, -1);
        assert_eq!(r.lines.len(), 4);
        let r = send_build(b"0", plen);
        assert_eq!(r.lines, vec![msg_txt(259).to_string()]);
        let r = send_build(b"1", plen);
        assert_eq!(r.lines, vec!["Unknown packet: 0x1".to_string()]);
        let r = send_build(b"80 Z1", plen);
        assert_eq!(r.lines, vec![msg_txt(908).to_string(), ">Z1".to_string()]);
        let r = send_build(b"80 Lx", plen);
        assert_eq!(r.lines, vec!["Invalid number in:".to_string(), ">x".to_string()]);
    }

    #[test]
    fn send_static_and_dynamic() {
        let r = send_build(b"0x0080 L1234 B0", plen);
        assert_eq!(r.packet, Some(vec![0x80, 0x00, 0xd2, 0x04, 0x00, 0x00, 0x00]));
        assert_eq!(r.lines, vec!["Sent packet 0x80 (128)".to_string()]);
        // B then a space: GET_VALUE reads " 5" without moving, SKIP_VALUE lands on the 5 again
        let r = send_build(b"80 B 5", plen);
        assert_eq!(r.packet, Some(vec![0x80, 0x00, 5, 5, 0, 0, 0]));
        // the packet is cut at its length
        let r = send_build(b"2 W513 1", plen);
        assert_eq!(r.packet, Some(vec![0x02, 0x00, 0x01]));
        let r = send_build(b"8e S\"hi\"", plen);
        assert_eq!(r.packet, Some(vec![0x8e, 0x00, 6, 0, b'h', b'i']));
        // \x41: the first digit is shifted by 8, so the byte is the second digit's value
        let r = send_build(b"8e S\"\\x41\\101\\n\"", plen);
        assert_eq!(r.packet, Some(vec![0x8e, 0x00, 7, 0, 0x01, 0x41, 0x0a]));
        // a fixed-length string is padded with zeros
        let r = send_build(b"8e S4\"a\" Bx10", plen);
        assert_eq!(r.packet, Some(vec![0x8e, 0x00, 9, 0, b'a', 0, 0, 0, 0x10]));
        let r = send_build(b"8e S\"abc", plen);
        assert_eq!(r.lines, vec!["Unexpected end of string".to_string()]);
        let r = send_build(b"8e S3 \"a\"", plen);
        assert_eq!(r.lines, vec![msg_txt(906).to_string(), "> \"a\"".to_string()]);
    }

    #[test]
    fn langtypes() {
        assert_eq!(msg_langstr2langtype(b"eng"), 0);
        assert_eq!(msg_langstr2langtype(b"EN"), 0);
        assert_eq!(msg_langstr2langtype(b"e"), -1);
        assert_eq!(msg_langstr2langtype(b"por"), 8);
        assert_eq!(msg_langstr2langtype(b"xyz"), -1);
        assert_eq!(msg_checklangtype(0), 1);
        assert_eq!(msg_checklangtype(1), -2);
        assert_eq!(msg_checklangtype(9), -2);
        assert_eq!(msg_checklangtype(10), -1);
        assert_eq!(msg_checklangtype(-1), -1);
    }

    #[test]
    fn strtoll() {
        assert_eq!(c_strtoll(b" -42abc"), -42);
        assert_eq!(c_strtoll(b"abc"), 0);
        assert_eq!(c_strtoll(b"99999999999999999999"), i64::MAX);
        assert_eq!(c_strtoll(b"-99999999999999999999"), i64::MIN);
    }

    #[test]
    fn hex_scan() {
        assert_eq!(scan_hex(b"0x10", 8), Some(16));
        assert_eq!(scan_hex(b"0x", 8), Some(0));
        assert_eq!(scan_hex(b"-1", 8), Some(u64::MAX));
        assert_eq!(scan_hex(b"123456789", 8), Some(0x12345678));
        assert_eq!(scan_hex(b"g", 8), None);
    }
}
