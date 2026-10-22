//! Chat channel script commands (script.cpp "Channel System" [Cydh]): channel_create, channel_join,
//! channel_setopt, channel_getopt, channel_setcolor, channel_setpass, channel_setgroup / channel_setgroup2,
//! channel_chat, channel_ban, channel_unban, channel_kick, channel_delete — over the channel system of channel.rs.

use crate::channel::{self as chan, channel as _, Channel};
use crate::npc::npc as _;
use crate::script::buildins::*;
use crate::script::vm::*;
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "channel_create" => channel_create,
        "channel_join" => channel_join,
        "channel_setopt" => channel_setopt,
        "channel_getopt" => channel_getopt,
        "channel_setcolor" => channel_setcolor,
        "channel_setpass" => channel_setpass,
        "channel_setgroup" => channel_setgroup,
        "channel_chat" => channel_chat,
        "channel_ban" => channel_ban,
        "channel_unban" => channel_unban,
        "channel_kick" => channel_kick,
        "channel_delete" => channel_delete,
        _ => return None,
    })
}

fn put(c: &Ctx, ch: Channel) {
    c.db().channel().key().update(ch);
}

/// channel_name2channel(chname, nullptr, 0) → the channel row.
fn by_name(c: &Ctx, chname: &str) -> Option<Channel> {
    chan::channel_name2channel(c, chname, None, 0).and_then(|k| chan::get(c, &k))
}

/// safestrncpy into a CHAN_NAME_LENGTH buffer.
fn cut20(s: &str) -> String {
    s.chars().take(chan::CHAN_NAME_LENGTH - 1).collect()
}

/// channel_create "<chname>","<alias>"{,"<password>"{,<option>{,<delay>{,<color>{,<char id>}}}}}: a public channel,
/// or with a char id a private one the player joins. Pushes 1, or the channel_chk error / -5 / 0.
fn channel_create(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let chname = lat(&getstr(rt, st, 2));
    let i = chan::channel_chk(c, &chname, "", 3);
    if i != 0 {
        log::error!("buildin_channel_create: Channel name '{chname}' is invalid. Errno {i}");
        st.pushint(i as i64);
        return SCRIPT_CMD_FAILURE;
    }
    let mut sd = None;
    let (kind, private) = if st.hasdata(8) {
        let char_id = getnum(rt, st, 8) as u32;
        match crate::pc::charid2sd(c, char_id) {
            Some(u) => sd = Some((u.id, char_id)),
            None => {
                log::error!("buildin_channel_create: Player with char id '{}' is not found.", char_id as i32);
                st.pushint(-5);
                return SCRIPT_CMD_FAILURE;
            }
        }
        (chan::CHAN_TYPE_PRIVATE, true)
    } else {
        (chan::CHAN_TYPE_PUBLIC, false)
    };
    let alias = lat(&getstr(rt, st, 3));
    let mut pass = String::new();
    if st.hasdata(4) {
        let p = lat(&getstr(rt, st, 4));
        if !p.eq_ignore_ascii_case("null") {
            pass = cut20(&p);
        }
    }
    let opt = if st.hasdata(5) { getnum(rt, st, 5) as u8 } else if private { chan::PRIVATE_CHANNEL.opt } else { chan::CHAN_OPT_BASE };
    let msg_delay = if st.hasdata(6) { getnum(rt, st, 6) as u16 } else if private { chan::PRIVATE_CHANNEL.delay } else { 1000 };
    // private_channel.color is "Default" in conf/channels.conf
    let color = if st.hasdata(7) { getnum(rt, st, 7) as u32 } else { chan::channel_get_color("Default") };
    let tmp = Channel {
        key: String::new(),
        name: cut20(chname.get(1..).unwrap_or("")),
        pass,
        alias: cut20(&alias),
        kind,
        color,
        opt,
        msg_delay,
        char_id: sd.map(|(_, cid)| cid).unwrap_or(0),
        m: 0,
        gid: 0,
        users: Vec::new(),
        banned: Vec::new(),
        groups: Vec::new(),
    };
    let Some(key) = chan::channel_create(c, tmp) else {
        log::error!("buildin_channel_create: Cannot create channel '{chname}'.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if let Some((aid, _)) = sd {
        chan::channel_join(c, &key, aid);
    }
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// channel_join "<chname>"{,<char id>}: channel_join's answer (0, -1 invalid, -2 already in, -3 banned, -4 full).
fn channel_join(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let Some(aid) = charid2sd(rt, st, 3, "buildin_channel_join") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let chname = lat(&getstr(rt, st, 2));
    let Some(key) = chan::channel_name2channel(c, &chname, None, 0) else {
        log::error!("buildin_channel_join: Channel name '{chname}' is invalid.");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    st.pushint(chan::channel_join(c, &key, aid) as i64);
    SCRIPT_CMD_SUCCESS
}

const FLAG_OPTS: [u8; 7] = [chan::CHAN_OPT_ANNOUNCE_SELF, chan::CHAN_OPT_ANNOUNCE_JOIN, chan::CHAN_OPT_ANNOUNCE_LEAVE, chan::CHAN_OPT_COLOR_OVERRIDE, chan::CHAN_OPT_CAN_CHAT, chan::CHAN_OPT_CAN_LEAVE, chan::CHAN_OPT_AUTOJOIN];

/// channel_setopt "<chname>",<option>,<value>.
fn channel_setopt(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let chname = lat(&getstr(rt, st, 2));
    let opt = getnum(rt, st, 3);
    let value = getnum(rt, st, 4);
    let Some(mut ch) = by_name(c, &chname) else {
        log::error!("buildin_channel_setopt: Channel name '{chname}' is invalid.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if FLAG_OPTS.iter().any(|&o| o as i32 == opt) {
        if value != 0 {
            ch.opt |= opt as u8;
        } else {
            ch.opt &= !(opt as u8);
        }
    } else if opt == chan::CHAN_OPT_MSG_DELAY as i32 {
        ch.msg_delay = value as u16;
    } else {
        log::error!("buildin_channel_setopt: Invalid option {opt}!");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    put(c, ch);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// channel_getopt("<chname>",<option>).
fn channel_getopt(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let chname = lat(&getstr(rt, st, 2));
    let Some(ch) = by_name(c, &chname) else {
        log::error!("buildin_channel_getopt: Channel name '{chname}' is invalid.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let opt = getnum(rt, st, 3);
    if FLAG_OPTS.iter().any(|&o| o as i32 == opt) {
        st.pushint((ch.opt as i32 & opt != 0) as i64);
    } else if opt == chan::CHAN_OPT_MSG_DELAY as i32 {
        st.pushint(ch.msg_delay as i64);
    } else {
        log::error!("buildin_channel_getopt: Invalid option {opt}!");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

/// channel_setcolor "<chname>",<RGB color>.
fn channel_setcolor(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let chname = lat(&getstr(rt, st, 2));
    let color = getnum(rt, st, 3) as u32;
    let Some(mut ch) = by_name(c, &chname) else {
        log::error!("buildin_channel_setcolor: Channel name '{chname}' is invalid.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    ch.color = (color & 0x0000FF) << 16 | (color & 0x00FF00) | (color & 0xFF0000) >> 16; // RGB to BGR
    put(c, ch);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// channel_setpass "<chname>","<password>" ("null" clears it).
fn channel_setpass(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let chname = lat(&getstr(rt, st, 2));
    let passwd = lat(&getstr(rt, st, 3));
    let Some(mut ch) = by_name(c, &chname) else {
        log::error!("buildin_channel_setpass: Channel name '{chname}' is invalid.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    ch.pass = if passwd.eq_ignore_ascii_case("null") { String::new() } else { cut20(&passwd) };
    put(c, ch);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// channel_setgroup "<chname>",<group id>{,…} / channel_setgroup2 "<chname>",<array>: the groups that may join
/// (group 0 clears the list). Pushes the count given.
fn channel_setgroup(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let funcname = st.funcname.clone();
    let chname = lat(&getstr(rt, st, 2));
    let Some(mut ch) = by_name(c, &chname) else {
        log::error!("buildin_channel_setgroup: Channel name '{chname}' is invalid.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let n;
    if funcname.ends_with('2') {
        let d = st.data(3).clone();
        let Data::Name(id, idx, rf) = d else {
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        };
        let varname = name_of(id);
        if is_string_variable(&varname) {
            log::error!("buildin_channel_setgroup: The array {} is not numeric type.", lat(&varname));
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
        if not_server_variable(varname.first().copied().unwrap_or(0)) && rid2sd(rt, st, &format!("buildin_{funcname}")).is_none() {
            log::error!("buildin_{funcname}: Cannot use a player variable '{}' if no player is attached.", lat(&varname));
            return SCRIPT_CMD_FAILURE;
        }
        let sd = if not_server_variable(varname.first().copied().unwrap_or(0)) { Some(st.rid) } else { None };
        n = crate::script::reg::highest_key(rt, st, sd, &varname, &rf) as i32;
        if n < 1 {
            log::error!("buildin_channel_setgroup: No group id listed.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
        ch.groups.clear();
        for i in 0..n {
            let group = crate::script::reg::read_num(rt, st, sd, &varname, idx + i as u32, &rf) as i32;
            if group == 0 {
                put(c, ch);
                st.pushint(1);
                return SCRIPT_CMD_SUCCESS;
            }
            ch.groups.push(group as u16);
            log::info!("buildin_channel_setgroup: (2) Added group {group}. Num: {}", ch.groups.len());
        }
    } else {
        let group = getnum(rt, st, 3);
        n = st.lastdata() - 1;
        if n < 1 {
            log::error!("buildin_channel_setgroup: Please input at least 1 group_id.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
        ch.groups.clear();
        if group == 0 {
            // Removed group list
            put(c, ch);
            st.pushint(1);
            return SCRIPT_CMD_SUCCESS;
        }
        for i in 3..n + 2 {
            ch.groups.push(getnum(rt, st, i) as u16);
            log::info!("buildin_channel_setgroup: (1) Added group {}. Num: {}", ch.groups.last().unwrap(), ch.groups.len());
        }
    }
    put(c, ch);
    st.pushint(n as i64);
    SCRIPT_CMD_SUCCESS
}

/// channel_chat "<chname>"|"<map>","<message>"{,<color>}: "<alias> <message>" to the channel (a map's local channel
/// by map name, or #map for the NPC's map).
fn channel_chat(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let chname = lat(&getstr(rt, st, 2));
    let msg = lat(&getstr(rt, st, 3));
    let ch = if !chname.starts_with('#') {
        // By Map
        let Some(m) = crate::data::maps().id_of(&chname) else {
            log::error!("buildin_channel_chat: Invalid map '{chname}'.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        };
        match chan::get(c, &chan::map_key(m)) {
            Some(ch) => ch,
            None => {
                log::debug!("buildin_channel_chat: Map '{chname}' doesn't have local channel yet.");
                st.pushint(0);
                return SCRIPT_CMD_SUCCESS;
            }
        }
    } else if chname[1..].eq_ignore_ascii_case(chan::map_tmpl_name()) {
        let nd = c.db().npc().id().find(st.oid);
        let Some(m) = nd.filter(|n| n.map >= 0).map(|n| n.map as u16) else {
            log::error!("buildin_channel_chat: Floating NPC needs map name instead '{chname}'.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        };
        match chan::get(c, &chan::map_key(m)) {
            Some(ch) => ch,
            None => {
                log::debug!("buildin_channel_chat: Map '{chname}' doesn't have local channel yet.");
                st.pushint(0);
                return SCRIPT_CMD_SUCCESS;
            }
        }
    } else {
        match by_name(c, &chname) {
            Some(ch) => ch,
            None => {
                log::error!("buildin_channel_chat: Channel name '{chname}' is invalid.");
                st.pushint(0);
                return SCRIPT_CMD_FAILURE;
            }
        }
    };
    let color = if st.hasdata(4) { getnum(rt, st, 4) as u32 } else { ch.color };
    let out: Vec<u8> = crate::net::wire::latin1(&format!("{} {}", ch.alias, msg)).into_iter().take(crate::net::CHAT_SIZE_MAX - 1).collect();
    chan::clif_channel_msg(c, &ch, &crate::net::wire::from_latin1(&out), color);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// channel_ban "<chname>",<char id>: the player (online or not) cannot join any more; an online one leaves.
fn channel_ban(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let chname = lat(&getstr(rt, st, 2));
    let char_id = getnum(rt, st, 3) as u32;
    let Some(ch) = by_name(c, &chname) else {
        log::error!("buildin_channel_ban: Channel name '{chname}' is invalid.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if ch.char_id == char_id {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    let tsd = crate::pc::charid2sd(c, char_id).map(|u| u.id);
    if tsd.map(|a| crate::pc_misc::pc_has_permission(c, a, crate::rules::PC_PERM_CHANNEL_ADMIN)).unwrap_or(false) {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    if !ch.banned.iter().any(|b| b.char_id == char_id) {
        let key = ch.key.clone();
        let (name, output);
        if let Some(a) = tsd {
            name = crate::pc::character_of(c, a).map(|x| x.name).unwrap_or_default();
            chan::channel_clean(c, &key, a, 0);
            output = crate::data::format_msg(crate::data::msg_txt(769), &[&ch.alias, &name]); // %s %s has been banned.
        } else {
            name = String::new();
            output = crate::data::format_msg(crate::data::msg_txt(769), &[&ch.alias, &"****"]);
        }
        // (channel_clean may have deleted an emptied channel: rAthena then writes into freed memory)
        if let Some(mut ch2) = chan::get(c, &key) {
            ch2.banned.push(chan::ChanBanEntry { char_id, char_name: name });
            let color = ch2.color;
            put(c, ch2.clone());
            chan::clif_channel_msg(c, &ch2, &output, color);
        }
    }
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// channel_unban "<chname>",<char id>.
fn channel_unban(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let chname = lat(&getstr(rt, st, 2));
    let char_id = getnum(rt, st, 3) as u32;
    let Some(mut ch) = by_name(c, &chname) else {
        log::error!("buildin_channel_unban: Channel name '{chname}' is invalid.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    if ch.char_id == char_id {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    if ch.banned.iter().any(|b| b.char_id == char_id) {
        let name = crate::pc::charid2sd(c, char_id).and_then(|u| crate::pc::character_of(c, u.id)).map(|x| x.name).unwrap_or_else(|| "****".to_string());
        let output = crate::data::format_msg(crate::data::msg_txt(770), &[&ch.alias, &name]); // %s %s has been unbanned
        ch.banned.retain(|b| b.char_id != char_id);
        let color = ch.color;
        put(c, ch.clone());
        chan::clif_channel_msg(c, &ch, &output, color);
    }
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// channel_kick "<chname>",<char id>|"<name>": the online player leaves the channel.
fn channel_kick(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let chname = lat(&getstr(rt, st, 2));
    let tsd = if isstring(rt, st, 3) {
        let name = lat(&getstr(rt, st, 3));
        match crate::pc::nick2sd(c, &name, false) {
            Some(u) => u.id,
            None => {
                log::error!("buildin_channel_kick: Player with nick '{name}' is not online");
                st.pushint(0);
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        let char_id = getnum(rt, st, 3);
        match crate::pc::charid2sd(c, char_id as u32) {
            Some(u) => u.id,
            None => {
                log::error!("buildin_channel_kick: Player with char_id '{char_id}' is not online");
                st.pushint(0);
                return SCRIPT_CMD_FAILURE;
            }
        }
    };
    let Some(key) = chan::channel_name2channel(c, &chname, Some(tsd), 0) else {
        log::error!("buildin_channel_kick: Channel name '{chname}' is invalid.");
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let Some(ch) = chan::get(c, &key) else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let tcid = crate::pc::get(c, tsd).map(|p| p.char_id).unwrap_or(0);
    if chan::channel_pc_haschan(c, tsd, &ch) < 0 || ch.char_id == tcid || crate::pc_misc::pc_has_permission(c, tsd, crate::rules::PC_PERM_CHANNEL_ADMIN) {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    let res = match ch.kind {
        chan::CHAN_TYPE_ALLY => chan::channel_pcquit(c, tsd, 3),
        chan::CHAN_TYPE_MAP => chan::channel_pcquit(c, tsd, 4),
        _ => chan::channel_clean(c, &key, tsd, 0),
    };
    if res == 0 {
        let name = crate::pc::character_of(c, tsd).map(|x| x.name).unwrap_or_default();
        let output = crate::data::format_msg(crate::data::msg_txt(889), &[&ch.alias, &name]); // "%s %s is kicked"
        // (the channel row as channel_clean left it; an emptied private channel is gone)
        if let Some(ch2) = chan::get(c, &key) {
            let color = ch2.color;
            chan::clif_channel_msg(c, &ch2, &output, color);
        }
    }
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// channel_delete "<chname>": channel_delete(ch, true)'s answer (-1 invalid).
fn channel_delete(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    chan::ensure_init(c);
    let chname = lat(&getstr(rt, st, 2));
    let Some(key) = chan::channel_name2channel(c, &chname, None, 0) else {
        log::error!("channel_delete: Channel name '{chname}' is invalid.");
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    st.pushint(chan::channel_delete(c, &key, true) as i64);
    SCRIPT_CMD_SUCCESS
}
