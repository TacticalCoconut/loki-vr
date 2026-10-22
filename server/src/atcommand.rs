//! map/atcommand.cpp: @commands and #commands. `is_atcommand` is the full dispatcher (symbols, groups, aliases,
//! restrictions, "%s failed." / "Unknown Command" replies); each command is ported with the system it belongs to.
//! A command rAthena has but this module has not ported yet answers so instead of pretending to work.

use crate::clif;
use crate::cstr::{atoi, Scan};
use crate::data::{self, msg_txt};
use crate::npc::{self, MF_NOCOMMAND, MF_NOGO, MF_NOTELEPORT, MF_NOWARP, MF_NOWARPTO};
use crate::pc::{self, SetPos};
use crate::rules::{self, PC_PERM_DISABLE_CMD_DEAD, PC_PERM_HIDE_SESSION, PC_PERM_WARP_ANYWHERE, PC_PERM_WHO_DISPLAY_AID};
use crate::unit::{self, Unit, CLR_RESPAWN, CLR_TELEPORT};
use crate::world::Ctx;

/// conf/battle/gm.conf `atcommand_symbol: @`, `charcommand_symbol: #`.
pub const ATCOMMAND_SYMBOL: char = '@';
pub const CHARCOMMAND_SYMBOL: char = '#';

/// atcommand.hpp e_atcmd_restriction.
const ATCMD_NOCONSOLE: u8 = 1;
const ATCMD_NOSCRIPT: u8 = 2;
const ATCMD_NOAUTOTRADE: u8 = 4;

/// Where a command's replies go (the invoker's fd in rAthena).
pub(crate) struct Out<'a> {
    c: &'a Ctx<'a>,
    fd: Option<Unit>,
}

impl Out<'_> {
    pub(crate) fn say(&self, text: &str) {
        if let Some(u) = &self.fd {
            clif::displaymessage(self.c, u, text);
        }
    }
}

/// atcommand.cpp is_atcommand. `kind`: 0 script, 1 player, 2 console, 3 script with the player's restrictions.
/// Returns true when the message was a command (handled or refused) and must not be shown as chat.
pub fn is_atcommand(c: &Ctx, aid: u32, message: &str, kind: i32) -> bool {
    let Some(sd) = unit::get(c, aid) else { return false };
    let Some(p) = pc::get(c, aid) else { return false };
    if message.is_empty() {
        return false;
    }
    // If cannot use atcomamnd while talking with NPC [Kichi] (atcommand.cpp:11961)
    if kind == 1 && p.npc_id != 0 && crate::script::npc_run::sdx(c, aid).disable_atcommand_on_npc {
        return false;
    }
    // Block NOCHAT but do not display it as a normal message (atcommand.cpp:11965)
    const MANNER_NOCOMMAND: i32 = 0x04; // status.hpp manner_flags
    if crate::status::sc::get_sce(c, aid, crate::status::consts::SC_NOCHAT).map(|e| e.val1 & MANNER_NOCOMMAND != 0).unwrap_or(false) {
        return true;
    }
    let mut message = message;
    // skip 10/11-langtype's codepage indicator, if detected
    {
        let b: Vec<char> = message.chars().take(4).collect();
        if b.first() == Some(&'|') && message.chars().count() >= 4 && (b[3] == ATCOMMAND_SYMBOL || b[3] == CHARCOMMAND_SYMBOL) {
            let skip: usize = message.chars().take(3).map(|ch| ch.len_utf8()).sum();
            message = &message[skip..];
        }
    }
    let first = message.chars().next().unwrap_or('\0');
    if first != ATCOMMAND_SYMBOL && first != CHARCOMMAND_SYMBOL {
        return false;
    }
    let out = Out { c, fd: Some(sd.clone()) };
    let group_level = rules::group_level(p.group_id);
    if kind == 1 || kind == 3 {
        // Commands are disabled on maps flagged as 'nocommand'
        if group_level < npc::mapflag_value(c, sd.map, MF_NOCOMMAND) {
            out.say(msg_txt(143));
            return false;
        }
    }
    let is_at = first == ATCOMMAND_SYMBOL;
    let cmds = data::atcommands();
    let (atcmd_msg, ssd) = if is_at {
        (message.to_string(), sd.clone())
    } else {
        // #command "<char name>" <params> or #command <char name> <params>
        let b = crate::net::wire::latin1(message);
        let quoted = {
            let mut s = Scan::new(&b);
            let command = s.word(255);
            let mut n = command.is_some() as usize;
            let mut charname = None;
            let mut params = None;
            if command.is_some() {
                s.ws();
                if s.lit(b'"') {
                    charname = s.set(23, b"\"");
                    if charname.is_some() {
                        n += 1;
                        if s.lit(b'"') {
                            s.ws();
                            params = s.set(255, b"\n");
                            n += params.is_some() as usize;
                        }
                    }
                }
            }
            (n, command, charname, params)
        };
        let (n, command, charname, params) = if quoted.0 >= 2 {
            quoted
        } else {
            let mut s = Scan::new(&b);
            let command = s.word(255);
            let charname = command.as_ref().and_then(|_| s.word(23));
            let params = charname.as_ref().and_then(|_| {
                s.ws();
                s.set(255, b"\n")
            });
            (command.is_some() as usize + charname.is_some() as usize + params.is_some() as usize, command, charname, params)
        };
        if n < 2 {
            if group_level == 0 {
                let Some(command) = command else { return false };
                let name = cmds.check_alias(&command[1..]);
                match cmds.info(name) {
                    Some(info) if rules::can_use_command(p.group_id, &info.0, true) => {}
                    _ => return false,
                }
            }
            out.say(&data::format_msg(msg_txt(1388), &[&CHARCOMMAND_SYMBOL])); // Charcommand failed (usage: ...)
            return true;
        }
        let command = command.unwrap_or_default();
        let charname = charname.unwrap_or_default();
        let Some(target) = pc::nick2sd(c, &charname, true) else {
            out.say(&data::format_msg(msg_txt(1389), &[&command])); // %s failed. Player not found.
            return true;
        };
        let m = match params {
            Some(params) if n > 2 => format!("{command} {params}"),
            _ => command,
        };
        (m, target)
    };
    // IDLE_ATCOMMAND (0x0200) is not in idletime_option
    // split "<command> <params>"
    let b = crate::net::wire::latin1(&atcmd_msg);
    let mut s = Scan::new(&b);
    let command = s.word(255).unwrap_or_default();
    let params = {
        s.ws();
        s.set(255, b"\n").unwrap_or_default()
    };
    if kind == 1 && p.block_action & pc::PCBLOCK_COMMANDS != 0 {
        out.say(&data::format_msg(msg_txt(154), &[&command])); // %s failed.
        return true;
    }
    // @commands (script based): bindatcmd (script2/admin.rs)
    if kind == 1 || kind == 3 {
        if let Some(binding) = crate::script::buildins::script2::admin::get_atcommandbind_byname(c, &command) {
            if !binding.npc_event.is_empty() && ((is_at && group_level >= binding.level) || (!is_at && group_level >= binding.level2)) {
                // Check if self or character invoking; if self == character invoked, then self invoke.
                crate::script::buildins::script2::admin::npc_do_atcmd_event(c, ssd.id, &command, &params, &binding.npc_event);
                return true;
            }
        }
    }
    let name = if command.len() > 1 { cmds.check_alias(&command[1..]) } else { "" };
    let Some(info) = cmds.info(name) else {
        if group_level == 0 {
            return false;
        }
        out.say(&data::format_msg(msg_txt(153), &[&command])); // "%s is Unknown Command."
        // feature.atcommand_suggestions: off
        return true;
    };
    let restriction = info.2;
    if restriction != 0 {
        if restriction & ATCMD_NOCONSOLE != 0 && kind == 2 {
            return true;
        }
        if restriction & ATCMD_NOSCRIPT != 0 && (kind == 0 || kind == 3) {
            return true;
        }
        if restriction & ATCMD_NOAUTOTRADE != 0
            && (kind == 0 || kind == 3)
            && ((is_at && crate::autotrade::autotrade(c, aid) != 0) || crate::autotrade::autotrade(c, ssd.id) != 0)
        {
            return true;
        }
    }
    if kind == 1 {
        if !rules::can_use_command(p.group_id, &info.0, !is_at) {
            return false;
        }
        if p.dead_sit == 1 && pc::has_permission(c, aid, PC_PERM_DISABLE_CMD_DEAD) {
            out.say(msg_txt(1393)); // You can't use commands while dead
            return true;
        }
    }
    let r = run(c, &out, ssd.id, &info.1, &command, &params);
    if r != 0 {
        out.say(&data::format_msg(msg_txt(154), &[&command])); // %s failed.
        return true;
    }
    //Log only if successful.
    crate::map_log::log_atcommand(c, aid, if is_at { atcmd_msg.as_str() } else { message });
    true
}

/// Calls the command's function (the ACMD_FUNC named in the command table).
fn run(c: &Ctx, out: &Out, sd: u32, func: &str, command: &str, message: &str) -> i32 {
    match func {
        "mapmove" => mapmove(c, out, sd, message),
        "where" => where_(c, out, sd, message),
        "jumpto" => jumpto(c, out, sd, message),
        "jump" => jump(c, out, sd, message),
        "recall" => recall(c, out, sd, message),
        "go" => go(c, out, sd, command, message),
        "lokirnd" => lokirnd(c, out, message),
        f if crate::atcommand_status::handles(f) => crate::atcommand_status::run(c, &|t: &str| out.say(t), sd, f, command, message),
        _ => {
            // the item system's commands (atcommand_items.rs), the quest commands (atcommand_quest.rs), then
            // @who…, channels, parties, @noask, @leave (atcommand_social.rs)
            if let Some(r) = crate::atcommand_items::run(c, out.fd.as_ref(), sd, func, command, message) {
                return r;
            }
            // the item windows' commands (itemui/atcommand.rs)
            if let Some(r) = crate::itemui::atcommand::run(c, out.fd.as_ref(), sd, func, command, message) {
                return r;
            }
            // @roulette (roulette.rs; after @enchantgradeui in atcommand_base)
            if let Some(r) = crate::roulette::atcommand(c, out.fd.as_ref(), sd, func) {
                return r;
            }
            if let Some(r) = crate::atcommand_quest::run(c, out, sd, func, command, message) {
                return r;
            }
            if let Some(r) = crate::mob::cmd::run(c, out, sd, func, command, message) {
                return r;
            }
            // @makeegg, @hatch, @petfriendly, @pethungry, @petrename, @pettalk (pet/cmd.rs)
            if let Some(r) = crate::pet::cmd::run(c, out, sd, func, command, message) {
                return r;
            }
            // @homlevel, @homevolution, @hommutate, @makehomun, @homfriendly, @homhungry, @homtalk, @hominfo,
            // @homstats, @homshuffle (homunculus/cmd.rs)
            if let Some(r) = crate::homunculus::cmd::run(c, out, sd, func, command, message) {
                return r;
            }
            if let Some(r) = crate::atcommand_social::run(c, out.fd.as_ref(), sd, func, command, message) {
                return r;
            }
            if let Some(r) = crate::atcommand_guild::run(c, out.fd.as_ref(), sd, func, command, message) {
                return r;
            }
            // the skill-tree and job commands (atcommand_skill.rs)
            if let Some(r) = crate::atcommand_skill::run(c, &|t: &str| out.say(t), sd, func, command, message) {
                return r;
            }
            // @resetcooltime, @useskill, @displayskill, @displayskillcast, @nuke (skill/atcommand.rs)
            if let Some(r) = crate::skill::atcommand::run(c, &|t: &str| out.say(t), sd, func, command, message) {
                return r;
            }
            // @displayskillunit (skillunit/atcommand.rs)
            if let Some(r) = crate::skillunit::atcommand::run(c, &|t: &str| out.say(t), sd, func, command, message) {
                return r;
            }
            // @addfame, @clanspy (atcommand_extras.rs)
            if let Some(r) = crate::atcommand_extras::run(c, out, sd, func, command, message) {
                return r;
            }
            // @mail, @trade, @cash, @points (atcommand_social2.rs)
            if let Some(r) = crate::atcommand_social2::run(c, out, sd, func, command, message) {
                return r;
            }
            // @autotrade, @duel, @invite, @accept, @reject (atcommand_vending.rs; @leave: atcommand_social.rs)
            if let Some(r) = crate::atcommand_vending::run(c, out, sd, func, command, message) {
                return r;
            }
            // @kill, @alive, @pvpon / off, @gvgon / off, @doom(map), @raise(map), @killer, @killable (battle)
            if let Some(r) = crate::battle::atcommand::run(c, out.fd.as_ref(), sd, func, command, message) {
                return r;
            }
            // @reloadinstancedb (atcommand_instance.rs)
            if let Some(r) = crate::atcommand_instance::run(c, out, sd, func, command, message) {
                return r;
            }
            // @save, @load, @memo, @kami…, @broadcast, @npctalk, @hide, @option, @speed, @kick, @mute… (script2)
            if let Some(r) = crate::atcommand_script2::run(c, out.fd.as_ref(), sd, func, command, message) {
                return r;
            }
            // @agitstart / @agitend and their 2 / 3 forms (woe/atcommand.rs)
            if let Some(r) = crate::woe::atcommand::run(c, out.fd.as_ref(), sd, func, command, message) {
                return r;
            }
            // the generic commands of wave D (atcmd agent: atcommand_atcmd.rs and its group files)
            if let Some(r) = crate::atcommand_atcmd::run(c, out.fd.as_ref(), sd, func, command, message) {
                return r;
            }
            // commands of systems not ported yet: a stub naming the owner (atcmd_stubs.rs); keep this last
            crate::atcommand_atcmd::stubs::not_ported(c, out.fd.as_ref(), func, command)
        }
    }
}

/// Loki test rig (not rAthena): `@lokirnd <k>` fixes every roll ([`crate::world::DebugRng`]), `@lokirnd off` makes them
/// random again, `@lokirnd` shows the state: the same command and reply as the oracle's patched atcommand.cpp.
fn lokirnd(c: &Ctx, out: &Out, message: &str) -> i32 {
    use crate::world::{debug_rng, DebugRng};
    use spacetimedb::Table;
    let m = message.trim();
    if !m.is_empty() {
        c.db().debug_rng().id().delete(0);
        if !m.eq_ignore_ascii_case("off") {
            let k = crate::cstr::atoi(m);
            if k >= 0 {
                c.db().debug_rng().insert(DebugRng { id: 0, k: k as u32 });
            }
        }
    }
    let k = c.db().debug_rng().id().find(0).map(|r| r.k as i32).unwrap_or(-1);
    out.say(&format!("lokirnd: {k}"));
    0
}

// ---------------------------------------------------------------------------------------------- warping

/// ACMD_FUNC(mapmove): @rura, @warp, @mapmove.
fn mapmove(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let Some(p) = pc::get(c, aid) else { return -1 };
    let b = crate::net::wire::latin1(message);
    let (mut map_name, mut x, mut y) = (String::new(), 0i16, 0i16);
    // sscanf(message, "%15s %6hd %6hd", map_name, &x, &y) < 3
    let n1 = {
        let mut s = Scan::new(&b);
        let mut n = 0;
        if let Some(m) = s.word(15) {
            map_name = m;
            n += 1;
            if let Some(v) = s.short(6) {
                x = v;
                n += 1;
                if let Some(v) = s.short(6) {
                    y = v;
                    n += 1;
                }
            }
        }
        n
    };
    // && sscanf(message, "%15[^,],%6hd,%6hd", map_name, &x, &y) < 1
    let bad = n1 < 3 && {
        let mut s = Scan::new(&b);
        let mut n = 0;
        if let Some(m) = s.set(15, b",") {
            map_name = m;
            n += 1;
            if s.lit(b',') {
                if let Some(v) = s.short(6) {
                    x = v;
                    if s.lit(b',') {
                        if let Some(v) = s.short(6) {
                            y = v;
                        }
                    }
                }
            }
        }
        n < 1
    };
    if message.is_empty() || bad {
        out.say(msg_txt(909)); // Please enter a map (usage: @warp/@rura/@mapmove <mapname> <x> <y>).
        return -1;
    }
    let Some(m) = data::maps().id_of(&map_name) else {
        out.say(msg_txt(1)); // Map not found.
        // feature.warp_suggestions: off
        return -1;
    };
    if (x != 0 || y != 0) && crate::map::getcell(c, m, x, y, crate::map::CellChk::ChkNoPass) {
        out.say(msg_txt(2)); // Invalid coordinates, using random target cell.
        match crate::map::search_freecell(c, m, x, y, 10, 10, 1) {
            Some((nx, ny)) => (x, y) = (nx, ny),
            None => (x, y) = (0, 0),
        }
    }
    let any_warp = pc::has_permission(c, aid, PC_PERM_WARP_ANYWHERE);
    let class = pc::character_of(c, aid).map(|ch| ch.class as i32).unwrap_or(0);
    if (npc::mapflag(c, m, MF_NOWARPTO) && !any_warp) || !pc::job_can_entermap(c, class, m, rules::group_level(p.group_id)) {
        out.say(msg_txt(247)); // You are not authorized to warp to this map.
        return -1;
    }
    if npc::mapflag(c, sd.map, MF_NOWARP) && !any_warp {
        out.say(msg_txt(248)); // You are not authorized to warp from your current map.
        return -1;
    }
    if pc::setpos(c, aid, m, x, y, CLR_TELEPORT) != SetPos::Ok {
        out.say(msg_txt(1)); // Map not found.
        return -1;
    }
    out.say(msg_txt(0)); // Warped.
    0
}

/// `%23[^\n]` of a message.
fn player_name_arg(message: &str) -> Option<String> {
    let b = crate::net::wire::latin1(message);
    Scan::new(&b).set(23, b"\n")
}

/// ACMD_FUNC(where).
fn where_(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(name) = player_name_arg(message) else {
        out.say(msg_txt(910)); // Please enter a player name (usage: @where <char name>).
        return -1;
    };
    let me = pc::get(c, aid);
    let found = pc::nick2sd(c, &name, true).filter(|pl| {
        let n = |s: &str| crate::net::wire::latin1(s).into_iter().take(24).collect::<Vec<u8>>();
        if n(&pl.name) != n(&name) {
            return false;
        }
        let Some(pp) = pc::get(c, pl.id) else { return false };
        let my_group = me.as_ref().map(|m| m.group_id).unwrap_or(0);
        !(pc::has_permission(c, pl.id, PC_PERM_HIDE_SESSION)
            && rules::group_level(pp.group_id) > rules::group_level(my_group)
            && !pc::has_permission(c, aid, PC_PERM_WHO_DISPLAY_AID))
    });
    let Some(pl) = found else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    let map = data::maps().get(pl.map).map(|d| d.name.as_str()).unwrap_or("");
    out.say(&format!("{} {} {} {}", pl.name, map, pl.x, pl.y));
    0
}

/// ACMD_FUNC(jumpto): @jumpto, @warpto, @goto.
fn jumpto(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let Some(name) = player_name_arg(message) else {
        out.say(msg_txt(911)); // Please enter a player name (usage: @jumpto/@warpto/@goto <char name/ID>).
        return -1;
    };
    let Some(pl) = pc::nick2sd(c, &name, true).or_else(|| pc::charid2sd(c, atoi(&name) as u32)) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    let any_warp = pc::has_permission(c, aid, PC_PERM_WARP_ANYWHERE);
    if npc::mapflag(c, pl.map, MF_NOWARPTO) && !any_warp {
        out.say(msg_txt(247));
        return -1;
    }
    if npc::mapflag(c, sd.map, MF_NOWARP) && !any_warp {
        out.say(msg_txt(248));
        return -1;
    }
    if pc::isdead(c, aid) {
        out.say(msg_txt(664)); // You cannot use this command when dead.
        return -1;
    }
    pc::setpos(c, aid, pl.map, pl.x, pl.y, CLR_TELEPORT);
    out.say(&data::format_msg(msg_txt(4), &[&pl.name])); // Jumped to %s
    0
}

/// ACMD_FUNC(jump).
fn jump(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let b = crate::net::wire::latin1(message);
    let (mut x, mut y) = (0i16, 0i16);
    {
        let mut s = Scan::new(&b);
        if let Some(v) = s.short(6) {
            x = v;
            if let Some(v) = s.short(6) {
                y = v;
            }
        }
    }
    if npc::mapflag(c, sd.map, MF_NOTELEPORT) && !pc::has_permission(c, aid, PC_PERM_WARP_ANYWHERE) {
        out.say(msg_txt(248));
        return -1;
    }
    if pc::isdead(c, aid) {
        out.say(msg_txt(664));
        return -1;
    }
    if (x != 0 || y != 0) && crate::map::getcell(c, sd.map, x, y, crate::map::CellChk::ChkNoPass) {
        out.say(msg_txt(2));
        match crate::map::search_freecell(c, sd.map, x, y, 10, 10, 1) {
            Some((nx, ny)) => (x, y) = (nx, ny),
            None => (x, y) = (0, 0),
        }
    }
    pc::setpos(c, aid, sd.map, x, y, CLR_TELEPORT);
    let sd = unit::get(c, aid).unwrap_or(sd);
    out.say(&data::format_msg(msg_txt(5), &[&sd.x, &sd.y])); // Jumped to %d %d
    0
}

/// ACMD_FUNC(recall).
fn recall(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let Some(name) = player_name_arg(message) else {
        out.say(msg_txt(1018)); // Please enter a player name (usage: @recall <char name/ID>).
        return -1;
    };
    let Some(pl) = pc::nick2sd(c, &name, true).or_else(|| pc::charid2sd(c, atoi(&name) as u32)) else {
        out.say(msg_txt(3));
        return -1;
    };
    if pc::group_level(c, aid) < pc::group_level(c, pl.id) {
        out.say(msg_txt(81)); // Your GM level doesn't authorize you to perform this action on the specified player.
        return -1;
    }
    let any_warp = pc::has_permission(c, aid, PC_PERM_WARP_ANYWHERE);
    if npc::mapflag(c, sd.map, MF_NOWARPTO) && !any_warp {
        out.say(msg_txt(1019)); // You are not authorized to warp someone to this map.
        return -1;
    }
    if npc::mapflag(c, pl.map, MF_NOWARP) && !any_warp {
        out.say(msg_txt(1020)); // You are not authorized to warp this player from their map.
        return -1;
    }
    if pl.map == sd.map && pl.x == sd.x && pl.y == sd.y {
        return -1;
    }
    if pc::setpos(c, pl.id, sd.map, sd.x, sd.y, CLR_RESPAWN) == SetPos::Autotrade {
        out.say(msg_txt(1025)); // The player is currently autotrading and cannot be recalled.
        return -1;
    }
    out.say(&data::format_msg(msg_txt(46), &[&pl.name])); // %s recalled!
    0
}

/// ACMD_FUNC(go): the renewal town list.
fn go(c: &Ctx, out: &Out, aid: u32, command: &str, message: &str) -> i32 {
    const DATA: [(&str, i16, i16); 37] = [
        ("prontera", 156, 191),
        ("morocc", 156, 93),
        ("geffen", 119, 59),
        ("payon", 162, 233),
        ("alberta", 192, 147),
        ("izlude", 128, 146), // Renewal
        ("aldebaran", 140, 131),
        ("xmas", 147, 134),
        ("comodo", 209, 143),
        ("yuno", 157, 51),
        ("amatsu", 198, 84),
        ("gonryun", 160, 120),
        ("umbala", 89, 157),
        ("niflheim", 21, 153),
        ("louyang", 217, 40),
        ("iz_int", 18, 26), // Training Grounds (Renewal)
        ("sec_pri", 23, 61),
        ("jawaii", 249, 127),
        ("ayothaya", 151, 117),
        ("einbroch", 64, 200),
        ("lighthalzen", 158, 92),
        ("einbech", 70, 95),
        ("hugel", 96, 145),
        ("rachel", 130, 110),
        ("veins", 216, 123),
        ("moscovia", 223, 184),
        ("mid_camp", 180, 240),
        ("manuk", 282, 138),
        ("splendide", 201, 147),
        ("brasilis", 182, 239),
        ("dicastes01", 198, 187),
        ("mora", 44, 151),
        ("dewata", 200, 180),
        ("malangdo", 140, 114),
        ("malaya", 242, 211),
        ("eclage", 110, 39),
        ("lasagna", 193, 182),
    ];
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let any_warp = pc::has_permission(c, aid, PC_PERM_WARP_ANYWHERE);
    if npc::mapflag(c, sd.map, MF_NOGO) && !any_warp {
        out.say(msg_txt(995)); // You cannot use @go on this map.
        return 0;
    }
    let mut town = atoi(message);
    let b = crate::net::wire::latin1(message);
    let map_name = Scan::new(&b).word(11);
    let Some(map_name) = map_name.filter(|_| !message.is_empty() && town >= 0 && (town as usize) < DATA.len()) else {
        out.say(msg_txt(38)); // Invalid location number, or name.
        if let Some(text) = data::atcommands().help_string(command) {
            out.say(text);
        }
        return -1;
    };
    // get possible name of the city
    let n = map_name.to_ascii_lowercase();
    let pre = |p: &str, len: usize| {
        let a = n.as_bytes();
        let b = p.as_bytes();
        (0..len).all(|i| a.get(i) == b.get(i)) // strncmp(map_name, p, len) == 0
    };
    let table: [(&[(&str, usize)], i32); 37] = [
        (&[("prontera", 3)], 0),
        (&[("morocc", 4), ("morroc", 4)], 1),
        (&[("geffen", 3)], 2),
        (&[("payon", 3)], 3),
        (&[("alberta", 3)], 4),
        (&[("izlude", 3)], 5),
        (&[("aldebaran", 3)], 6),
        (&[("lutie", 3), ("christmas", usize::MAX), ("xmas", 3), ("x-mas", 3)], 7),
        (&[("comodo", 3)], 8),
        (&[("juno", 3), ("yuno", 3)], 9),
        (&[("amatsu", 3)], 10),
        (&[("kunlun", 3), ("gonryun", 3)], 11),
        (&[("umbala", 3)], 12),
        (&[("niflheim", 3)], 13),
        (&[("louyang", 3)], 14),
        (&[("new_1-1", 3), ("startpoint", 3), ("beginning", 3)], 15),
        (&[("sec_pri", 3), ("prison", 3), ("jail", 3)], 16),
        (&[("jawaii", 3)], 17),
        (&[("ayothaya", 3)], 18),
        (&[("einbroch", 5)], 19),
        (&[("lighthalzen", 3)], 20),
        (&[("einbech", 5)], 21),
        (&[("hugel", 3)], 22),
        (&[("rachel", 3)], 23),
        (&[("veins", 3)], 24),
        (&[("moscovia", 3)], 25),
        (&[("mid_camp", 3)], 26),
        (&[("manuk", 3)], 27),
        (&[("splendide", 3)], 28),
        (&[("brasilis", 3)], 29),
        (&[("dicastes01", 3)], 30),
        (&[("mora", usize::MAX)], 31),
        (&[("dewata", 3)], 32),
        (&[("malangdo", 5)], 33),
        (&[("malaya", 5)], 34),
        (&[("eclage", 3)], 35),
        (&[("lasagna", 2)], 36),
    ];
    for (names, t) in table {
        // strcmp (usize::MAX) or strncmp(len); the first matching test in rAthena's if/else chain wins
        if names.iter().any(|&(p, len)| if len == usize::MAX { n == p } else { pre(p, len) }) {
            town = t;
            break;
        }
    }
    if town < 0 || town as usize >= DATA.len() {
        out.say(msg_txt(38));
        return -1;
    }
    let (map, x, y) = DATA[town as usize];
    let m = data::maps().id_of(map);
    if let Some(m) = m {
        if npc::mapflag(c, m, MF_NOWARPTO) && !any_warp {
            out.say(msg_txt(247));
            return -1;
        }
    }
    if npc::mapflag(c, sd.map, MF_NOWARP) && !any_warp {
        out.say(msg_txt(248));
        return -1;
    }
    match m.map(|m| pc::setpos(c, aid, m, x, y, CLR_TELEPORT)) {
        Some(SetPos::Ok) => {
            out.say(msg_txt(0)); // Warped.
            0
        }
        _ => {
            out.say(msg_txt(1)); // Map not found.
            -1
        }
    }
}
