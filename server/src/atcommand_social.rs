//! map/atcommand.cpp: the social @commands — @who / @who2 / @who3 / @whomap / @whomap2 / @whomap3 (and @whois),
//! the channel commands (@join, @channel alias @main, @fontcolor), the party commands (@party, @partyoption,
//! @changeleader, @partysharelvl, @partyrecall), @noask and the duel command @leave. atcommand::run routes the
//! ACMD_FUNC names here.

use crate::clif;
use crate::cstr::{atoi, Scan};
use crate::data::{format_msg, msg_txt};
use crate::net::wire::latin1;
use crate::pc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

struct Out<'a> {
    c: &'a Ctx<'a>,
    fd: Option<&'a Unit>,
}

impl Out<'_> {
    fn say(&self, text: &str) {
        if let Some(u) = self.fd {
            clif::displaymessage(self.c, u, text);
        }
    }
}

/// Calls the ACMD_FUNC `func` for player `sd` (replies go to `fd`, the invoker). None when `func` is not one of these.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "who" => who(c, &out, sd, command, message),
        "join" => join(c, &out, sd, command, message),
        "channel" => channel(c, &out, sd, command, message),
        "fontcolor" => fontcolor(c, &out, sd, message),
        "leave" => leave(c, &out, sd),
        "noask" => noask(c, &out, sd),
        "party" => party(c, &out, sd, message),
        "partyoption" => partyoption(c, &out, sd, message),
        "changeleader" => changeleader(c, &out, sd, message),
        "partysharelvl" => partysharelvl(&out, message),
        "partyrecall" => partyrecall(c, &out, sd, message),
        _ => return None,
    })
}

/// strlib.cpp stristr != nullptr (case-insensitive substring; an empty needle matches).
fn stristr(hay: &str, needle: &str) -> bool {
    let h = latin1(hay);
    let n = latin1(needle);
    if n.is_empty() {
        return true;
    }
    h.windows(n.len()).any(|w| w.eq_ignore_ascii_case(&n))
}

/// ACMD_FUNC(who): 1 = @who (name, title, party, guild), 2 = @who2 (levels, job), 3 = @who3 (location).
fn who(c: &Ctx, out: &Out, aid: u32, command: &str, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let b = latin1(message);
    let mut player_name = String::new();
    let mut map_id: Option<u16> = None;
    if command.contains("map") {
        let mut s = Scan::new(&b);
        let map_name = s.word(15);
        if let Some(n) = map_name.as_ref() {
            if let Some(p) = s.word(23) {
                player_name = p;
            }
            map_id = crate::data::maps().id_of(n);
        }
        if map_id.is_none() {
            map_id = Some(sd.map);
        }
    } else if let Some(p) = Scan::new(&b).word(23) {
        player_name = p;
    }
    let display_type = if command.contains('2') {
        2
    } else if command.contains('3') {
        3
    } else {
        1
    };
    let level = pc::group_level(c, aid);
    let mut count = 0;
    for pl in crate::social::all_pc(c) {
        let Some(pp) = pc::get(c, pl.id) else { continue };
        let hidden = crate::pc::has_permission(c, pl.id, crate::rules::PC_PERM_HIDE_SESSION) || pl.sc.option & crate::unit::OPTION_INVISIBLE != 0;
        if hidden && crate::rules::group_level(pp.group_id) > level {
            continue; // you can look only lower or same level
        }
        let Some(ch) = pc::character_of(c, pl.id) else { continue };
        if !stristr(&ch.name, &player_name) || map_id.is_some_and(|m| pl.map != m) {
            continue;
        }
        let title = if pp.group_id > 0 { crate::rules::group(pp.group_id).map(|g| g.name.clone()) } else { None };
        let mut buf = String::new();
        match display_type {
            2 => {
                buf += &format_msg(msg_txt(343), &[&ch.name]); // "Name: %s "
                if let Some(t) = &title {
                    buf += &format_msg(msg_txt(344), &[t]); // "(%s) "
                }
                buf += &format_msg(msg_txt(347), &[&ch.base_level, &ch.job_level, &crate::db::jobs::job_name(ch.class as i32)]); // "| Lv:%d/%d | Job: %s"
            }
            3 => {
                if pc::has_permission(c, aid, crate::rules::PC_PERM_WHO_DISPLAY_AID) {
                    buf += &format_msg(msg_txt(912), &[&ch.id, &ch.account_id]); // "(CID:%d/AID:%d) "
                }
                buf += &format_msg(msg_txt(343), &[&ch.name]);
                if let Some(t) = &title {
                    buf += &format_msg(msg_txt(344), &[t]);
                }
                let mapname = pp.mapindex.and_then(|m| crate::data::maps().get(m)).map(|d| d.name.clone()).unwrap_or_default();
                buf += &format_msg(msg_txt(348), &[&mapname, &pl.x, &pl.y]); // "| Location: %s %d %d"
            }
            _ => {
                buf += &format_msg(msg_txt(343), &[&ch.name]);
                if let Some(t) = &title {
                    buf += &format_msg(msg_txt(344), &[t]);
                }
                if let Some(p) = crate::party::party_search(c, ch.party_id) {
                    buf += &format_msg(msg_txt(345), &[&p.party.name]); // " | Party: '%s'"
                }
                if let Some(g) = crate::guild::sd_guild(c, pl.id) {
                    buf += &format_msg(msg_txt(346), &[&g.guild.name]); // " | Guild: '%s'"
                }
            }
        }
        out.say(&buf);
        count += 1;
    }
    let tail = match map_id {
        None => match count {
            0 => msg_txt(28).to_string(), // No player found.
            1 => msg_txt(29).to_string(), // 1 player found.
            n => format_msg(msg_txt(30), &[&n]), // %d players found.
        },
        Some(m) => {
            let name = crate::data::maps().get(m).map(|d| d.name.clone()).unwrap_or_default();
            match count {
                0 => format_msg(msg_txt(54), &[&name]), // No player found in map '%s'.
                1 => format_msg(msg_txt(55), &[&name]), // 1 player found in map '%s'.
                n => format_msg(msg_txt(56), &[&n, &name]), // %d players found in map '%s'.
            }
        }
    };
    out.say(&tail);
    0
}

/// ACMD_FUNC(join): @join <#channel_name> [password].
fn join(c: &Ctx, out: &Out, aid: u32, command: &str, message: &str) -> i32 {
    crate::channel::ensure_init(c);
    let b = latin1(message);
    let mut s = Scan::new(&b);
    let Some(chname) = s.word(19).filter(|_| !message.is_empty()) else {
        out.say(&format_msg(msg_txt(1399), &[&command])); // Unknown channel (usage: %s <#channel_name>).
        return -1;
    };
    let pass = s.word(19);
    crate::channel::channel_pcjoin(c, aid, &chname, pass.as_deref())
}

/// atcommand.cpp atcmd_channel_help.
fn atcmd_channel_help(c: &Ctx, out: &Out, aid: u32, command: &str) {
    let can_delete = pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN);
    let can_create = can_delete || crate::channel::PRIVATE_CHANNEL.allow;
    let m = |n: u16| format_msg(msg_txt(n), &[&command]);
    out.say(msg_txt(1414)); // ---- Available options:
    if can_create {
        out.say(&m(1415));
        out.say(msg_txt(1416));
    }
    if can_delete {
        out.say(&m(1469));
        out.say(msg_txt(1470));
    }
    out.say(&m(1417));
    out.say(msg_txt(1418));
    out.say(&m(1471));
    out.say(msg_txt(1472));
    if can_create {
        out.say(&m(1419));
        out.say(msg_txt(1420));
    }
    if can_create {
        out.say(&m(1421));
        out.say(msg_txt(1422));
    }
    out.say(&m(1473));
    out.say(msg_txt(1474));
    out.say(&m(1423));
    out.say(msg_txt(1424));
    out.say(&m(1427));
    out.say(msg_txt(1428));
    out.say(&m(1429));
    out.say(msg_txt(1430));
    if can_create {
        out.say(&m(1456));
        out.say(msg_txt(1457));
        out.say(&m(1458));
        out.say(msg_txt(1459));
        out.say(&m(1460));
        out.say(msg_txt(1461));
        out.say(&m(1467));
        out.say(msg_txt(1468));
    }
    if can_create {
        out.say(&m(1462));
        out.say(msg_txt(1463));
    }
    out.say(&m(1404)); // %s failed.
}

/// ACMD_FUNC(channel): @channel <option> …
fn channel(c: &Ctx, out: &Out, aid: u32, command: &str, message: &str) -> i32 {
    use crate::channel::*;
    ensure_init(c);
    let b = latin1(message);
    // sscanf(message, "%23s %19s %63[^\n]", key, sub1, sub2)
    let mut s = Scan::new(&b);
    let key = s.word(23);
    let Some(key) = key.filter(|_| !message.is_empty()) else {
        atcmd_channel_help(c, out, aid, command);
        return 0;
    };
    let sub1 = s.word(19).unwrap_or_default();
    let sub2 = {
        s.ws();
        s.set(63, b"\n").unwrap_or_default()
    };
    let k = key.to_ascii_lowercase();
    let admin = pc::has_permission(c, aid, crate::rules::PC_PERM_CHANNEL_ADMIN);
    if k == "delete" && admin {
        return channel_pcdelete(c, aid, &sub1);
    } else if k == "list" {
        return channel_display_list(c, aid, &sub1);
    } else if k == "setcolor" {
        return channel_pccolor(c, aid, &sub1, &sub2);
    } else if k == "join" {
        return channel_pcjoin(c, aid, &sub1, Some(&sub2));
    } else if k == "leave" {
        return channel_pcleave(c, aid, &sub1);
    } else if k == "bindto" {
        return channel_pcbind(c, aid, &sub1);
    } else if k == "unbind" {
        return channel_pcunbind(c, aid);
    } else if k == "ban" {
        return channel_pcban(c, aid, &sub1, &sub2, 0);
    } else if k == "kick" {
        return channel_pckick(c, aid, &sub1, &sub2);
    } else if k == "banlist" {
        return channel_pcban(c, aid, &sub1, "", 3);
    } else if k == "unban" {
        return channel_pcban(c, aid, &sub1, &sub2, 1);
    } else if k == "unbanall" {
        return channel_pcban(c, aid, &sub1, "", 2);
    }
    // sscanf(sub2, "%19s %19s", sub3, sub4)
    let b2 = latin1(&sub2);
    let mut s2 = Scan::new(&b2);
    let sub3 = s2.word(19).unwrap_or_default();
    let sub4 = s2.word(19).unwrap_or_default();
    if k == "create" && (PRIVATE_CHANNEL.allow || admin) {
        if !sub4.is_empty() {
            out.say(msg_txt(1408)); // Channel password may not contain spaces.
            return -1;
        }
        return channel_pccreate(c, aid, &sub1, &sub3);
    } else if k == "setopt" {
        return channel_pcsetopt(c, aid, &sub1, &sub3, &sub4);
    }
    atcmd_channel_help(c, out, aid, command);
    0
}

/// ACMD_FUNC(fontcolor).
fn fontcolor(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    if message.is_empty() {
        crate::channel::channel_display_list(c, aid, "colors");
        return -1;
    }
    let mut x = crate::social::sx(c, aid);
    if message.eq_ignore_ascii_case("Normal") {
        x.fontcolor = 0;
    } else {
        let Some(k) = crate::channel::color_index(message) else {
            out.say(&format_msg(msg_txt(1411), &[&message])); // Unknown color '%s'.
            return -1;
        };
        x.fontcolor = k as u8;
    }
    crate::social::put_sx(c, &x);
    out.say(&format_msg(msg_txt(1454), &[&message])); // Color set to '%s'.
    0
}

/// ACMD_FUNC(leave): the duel system's @leave (duel.rs).
fn leave(c: &Ctx, out: &Out, aid: u32) -> i32 {
    crate::duel::atcommand_leave(c, &|t: &str| out.say(t), aid)
}

/// ACMD_FUNC(noask).
fn noask(c: &Ctx, out: &Out, aid: u32) -> i32 {
    let mut x = crate::social::sx(c, aid);
    if x.noask {
        out.say(msg_txt(391)); // Autorejecting is deactivated.
        x.noask = false;
    } else {
        out.say(msg_txt(390)); // Autorejecting is activated.
        x.noask = true;
    }
    crate::social::put_sx(c, &x);
    0
}

/// `%23[^\n]` of a message.
fn line23(message: &str) -> Option<String> {
    let b = latin1(message);
    Scan::new(&b).set(23, b"\n")
}

/// ACMD_FUNC(party).
fn party(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(name) = line23(message).filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1029)); // Please enter a party name (usage: @party <party_name>).
        return -1;
    };
    crate::party::party_create(c, aid, &latin1(&name), 0, 0);
    0
}

/// strlib.cpp:239 config_switch (battle_config/mod.rs has the port).
pub fn config_switch(s: &str) -> i32 {
    crate::battle_config::config_switch(&latin1(s))
}

/// ACMD_FUNC(partyoption): @partyoption <pickup share: yes/no> <item distribution: yes/no>.
fn partyoption(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(p) = crate::party::party_search(c, crate::party::party_id_of(c, aid)) else {
        out.say(msg_txt(282)); // You need to be a party leader to use this command.
        return -1;
    };
    let Some(mi) = p.data.iter().position(|d| d.sd == aid) else { return -1 }; // Shouldn't happen
    if !p.party.member[mi].leader {
        out.say(msg_txt(282));
        return -1;
    }
    let b = latin1(message);
    let mut s = Scan::new(&b);
    let (Some(w1), Some(w2)) = (s.word(15), s.word(15)) else {
        out.say(msg_txt(1186)); // Usage: @partyoption <pickup share: yes/no> <item distribution: yes/no>
        return -1;
    };
    let option = (config_switch(&w1) != 0) as i32 | (((config_switch(&w2) != 0) as i32) << 1);
    // Change item share type.
    if option != p.party.item as i32 {
        crate::party::party_changeoption(c, aid, p.party.exp as i32, option);
    } else {
        out.say(msg_txt(286)); // There's been no change in the setting.
    }
    0
}

/// ACMD_FUNC(changeleader).
fn changeleader(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(name) = line23(message).filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1185)); // Usage: @changeleader <party_member_name>
        return -1;
    };
    let t = pc::nick2sd(c, &name, false).map(|u| u.id);
    crate::party::party_changeleader(c, Some(aid), t, None);
    0
}

/// ACMD_FUNC(partysharelvl).
fn partysharelvl(out: &Out, message: &str) -> i32 {
    if message.is_empty() {
        out.say(msg_txt(1322)); // Please enter an amount.
        return -1;
    }
    let share_lvl = (atoi(message).unsigned_abs()).min(crate::chat::MAX_LEVEL);
    crate::intif::send(crate::intif::Inter::PartyShareLevel { share_lvl });
    out.say(msg_txt(1478)); // Party share level range has been changed successfully.
    0
}

/// ACMD_FUNC(partyrecall).
fn partyrecall(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let Some(party_name) = line23(message).filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1035)); // Please enter a party name/ID (usage: @partyrecall <party_name/ID>).
        return -1;
    };
    let any_warp = pc::has_permission(c, aid, crate::rules::PC_PERM_WARP_ANYWHERE);
    if crate::npc::mapflag(c, sd.map, crate::npc::MF_NOWARPTO) && !any_warp {
        out.say(msg_txt(1032)); // You are not authorized to warp someone to your current map.
        return -1;
    }
    // name first to avoid error when name begin with a number
    let Some(p) = crate::party::party_searchname(c, &party_name).or_else(|| crate::party::party_search(c, atoi(message) as u32)) else {
        out.say(msg_txt(96)); // Incorrect name or ID, or no one from the party is online.
        return -1;
    };
    let mut count = 0;
    let my_level = pc::group_level(c, aid);
    let mapindex = pc::get(c, aid).and_then(|p| p.mapindex).unwrap_or(sd.map);
    for pl in crate::social::all_pc(c) {
        if pl.id == aid || crate::party::party_id_of(c, pl.id) != p.party_id {
            continue;
        }
        if pc::group_level(c, pl.id) > my_level || (pl.map == sd.map && pl.x == sd.x && pl.y == sd.y) {
            continue; // Skip GMs greater than you... or chars already on the cell
        }
        if crate::npc::mapflag(c, pl.map, crate::npc::MF_NOWARP) && !any_warp {
            count += 1;
        } else {
            if pc::setpos(c, pl.id, mapindex, sd.x, sd.y, crate::unit::CLR_RESPAWN) == pc::SetPos::Autotrade {
                count += 1;
            }
        }
    }
    out.say(&format_msg(msg_txt(95), &[&p.party.name])); // All online characters of the %s party have been recalled to your position.
    if count != 0 {
        out.say(&format_msg(msg_txt(1033), &[&count])); // Because you are not authorized to warp from some maps, %d player(s) have not been recalled.
    }
    0
}
