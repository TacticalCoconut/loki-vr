//! atcommand.cpp's quest commands: @setquest, @erasequest, @completequest, @checkquest. (@reloadquestdb has nothing
//! to reload here: the quest db is embedded in the module.)

use crate::atcommand::Out;
use crate::cstr::Scan;
use crate::data::{format_msg, msg_txt};
use crate::quest::{self, HAVEQUEST, HUNTING, PLAYTIME, Q_COMPLETE};
use crate::world::Ctx;

/// The quest commands, by ACMD_FUNC name (None: not a quest command).
pub(crate) fn run(c: &Ctx, out: &Out, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    Some(match func {
        "setquest" => setquest(c, out, sd, command, message),
        "erasequest" => erasequest(c, out, sd, command, message),
        "completequest" => completequest(c, out, sd, command, message),
        "checkquest" => checkquest(c, out, sd, command, message),
        _ => return None,
    })
}

/// `!message || !*message || sscanf(message, "%11d", &quest_id) < 1 || quest_id == 0` → usage; then the quest db.
fn quest_arg(out: &Out, command: &str, message: &str) -> Option<i32> {
    let b = crate::net::wire::latin1(message);
    let quest_id = if message.is_empty() { None } else { Scan::new(&b).int(11).map(|v| v as i32) };
    let Some(quest_id) = quest_id.filter(|&q| q != 0) else {
        out.say(&format_msg(msg_txt(1505), &[&command])); // Usage: %s <quest ID>
        return None;
    };
    if crate::db::misc::quest::quest_search(quest_id).is_some() {
        Some(quest_id)
    } else {
        out.say(&format_msg(msg_txt(1506), &[&quest_id])); // Quest %d not found in DB.
        None
    }
}

/// ACMD_FUNC(setquest).
fn setquest(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some(quest_id) = quest_arg(out, command, message) else { return -1 };
    if quest::quest_check(c, sd, quest_id, HAVEQUEST) >= 0 {
        out.say(&format_msg(msg_txt(1507), &[&quest_id])); // Character already has quest %d.
        return -1;
    }
    quest::quest_add(c, sd, quest_id);
    quest::pc_show_questinfo(c, sd);
    0
}

/// ACMD_FUNC(erasequest).
fn erasequest(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some(quest_id) = quest_arg(out, command, message) else { return -1 };
    if quest::quest_check(c, sd, quest_id, HAVEQUEST) < 0 {
        out.say(&format_msg(msg_txt(1508), &[&quest_id])); // Character doesn't have quest %d.
        return -1;
    }
    quest::quest_delete(c, sd, quest_id);
    quest::pc_show_questinfo(c, sd);
    0
}

/// ACMD_FUNC(completequest): gives the quest first when the character does not have it.
fn completequest(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some(quest_id) = quest_arg(out, command, message) else { return -1 };
    if quest::quest_check(c, sd, quest_id, HAVEQUEST) < 0 {
        quest::quest_add(c, sd, quest_id);
    }
    if quest::quest_check(c, sd, quest_id, HAVEQUEST) < Q_COMPLETE as i32 {
        quest::quest_update_status(c, sd, quest_id, Q_COMPLETE);
    }
    quest::pc_show_questinfo(c, sd);
    0
}

/// ACMD_FUNC(checkquest).
fn checkquest(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some(quest_id) = quest_arg(out, command, message) else { return -1 };
    out.say(&format_msg(msg_txt(1509), &[&quest_id])); // Checkquest value for quest %d
    out.say(&format_msg(msg_txt(1510), &[&quest::quest_check(c, sd, quest_id, HAVEQUEST)])); // HAVEQUEST : %d
    out.say(&format_msg(msg_txt(1511), &[&quest::quest_check(c, sd, quest_id, HUNTING)])); // HUNTING   : %d
    out.say(&format_msg(msg_txt(1512), &[&quest::quest_check(c, sd, quest_id, PLAYTIME)])); // PLAYTIME  : %d
    0
}
