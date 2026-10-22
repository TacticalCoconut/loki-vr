//! atcommand.cpp's commands of the extras systems: @addfame (fame.rs) and @clanspy (clan.rs). (@reloadachievementdb /
//! @reloadattendancedb belong to the database reloads, which have no owner yet: the databases are embedded.)

use crate::atcommand::Out;
use crate::cstr::Scan;
use crate::data::{format_msg, msg_txt};
use crate::world::Ctx;

/// The extras commands, by ACMD_FUNC name (None: not one of them).
pub(crate) fn run(c: &Ctx, out: &Out, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    Some(match func {
        "addfame" => addfame(c, out, sd, command, message),
        "clanspy" => clanspy(out, message),
        _ => return None,
    })
}

/// ACMD_FUNC(addfame): @addfame <amount>.
fn addfame(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let b = crate::net::wire::latin1(message);
    let famepoint = if message.is_empty() { None } else { Scan::new(&b).int(11).map(|v| v as i32) };
    let Some(famepoint) = famepoint.filter(|&f| f != 0) else {
        out.say(&format_msg(msg_txt(1516), &[&command])); // Usage: %s <fame points>.
        return -1;
    };
    if !crate::fame::pc_addfame(c, sd, famepoint) {
        let class = crate::pc::character_of(c, sd).map(|ch| ch.class as i32).unwrap_or(0);
        out.say(&format_msg(msg_txt(1517), &[&crate::db::jobs::job_name(class)])); // Cannot add fame to class '%s'.
        return -1;
    }
    let name = crate::pc::character_of(c, sd).map(|ch| ch.name).unwrap_or_default();
    out.say(&format_msg(msg_txt(1518), &[&famepoint, &name])); // %d points were added to '%s'.
    0
}

/// ACMD_FUNC(clanspy): map_athena.conf `enable_spy: no`, so it stops at its first check.
fn clanspy(out: &Out, message: &str) -> i32 {
    if !crate::clan::ENABLE_SPY {
        out.say(msg_txt(1125)); // The mapserver has spy command support disabled.
        return -1;
    }
    // (with enable_spy: "%23[^\n]" of the message, clan_searchname then clan_search(atoi), sd->clanspy toggled
    // with msg 1500 / 1501, else msg 1502; the CLAN target then also reaches the spies)
    let _ = message;
    0
}
