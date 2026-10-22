//! atcommand.cpp's skill unit command: @displayskillunit (clif.cpp clif_skill_unit_test). Routed from atcommand.rs
//! `run` by the ACMD function name, like skill/atcommand.rs.

use crate::cstr::Scan;
use crate::data::msg_txt;
use crate::world::Ctx;

/// Runs the command when this file ports it (0 ok, -1 failed: the caller answers "%s failed.").
pub fn run(c: &Ctx, say: &dyn Fn(&str), aid: u32, func: &str, _command: &str, message: &str) -> Option<i32> {
    Some(match func {
        "displayskillunit" => displayskillunit(c, say, aid, message),
        _ => return None,
    })
}

/// `%6hu`: a number stored into an unsigned short (the C conversion's truncation).
fn hu(s: &mut Scan) -> Option<u16> {
    s.int(6).map(|v| v as u16)
}

/// ACMD_FUNC(displayskillunit): @displayskillunit <unit ID> {<skill level> <range>} — a fake unit of that id on the
/// GM's cell, shown to its area.
fn displayskillunit(c: &Ctx, say: &dyn Fn(&str), aid: u32, message: &str) -> i32 {
    // sscanf(message, "%6hu %6hu %6hu", &unit_id, &skill_lv, &range) < 1 (skill_lv 1 and range 0 stay when unread)
    let b = crate::net::wire::latin1(message);
    let mut s = Scan::new(&b);
    let mut skill_lv: u16 = 1;
    let mut range: u16 = 0;
    let unit_id = hu(&mut s);
    if let Some(v) = unit_id.and_then(|_| hu(&mut s)) {
        skill_lv = v;
        if let Some(r) = hu(&mut s) {
            range = r;
        }
    }
    let Some(unit_id) = unit_id.filter(|_| !message.is_empty()) else {
        say(msg_txt(826)); // Usage: @displayskillunit <unit ID> {<skill level> <range>}
        return -1;
    };
    let Some(sd) = crate::unit::get(c, aid) else { return -1 };
    // clif_skill_unit_test(sd, sd->x, sd->y, unit_id, range, skill_lv): int32 unit_id, int16 range and level
    super::clif::clif_skill_unit_test(c, &sd, sd.x, sd.y, unit_id as i32, range as i16, skill_lv as i16);
    0
}
