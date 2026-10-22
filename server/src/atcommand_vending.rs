//! map/atcommand.cpp: the vending agent's @commands — @autotrade (autotrade.rs) and the duel commands @duel,
//! @invite, @accept, @reject (duel.rs; @leave is routed by atcommand_social.rs, which had its name first).
//! atcommand::run routes the ACMD_FUNC names here.

use crate::atcommand::Out;
use crate::world::Ctx;

/// Calls the ACMD_FUNC `func` for player `sd`. None when `func` is not one of these.
pub(crate) fn run(c: &Ctx, out: &Out, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    let say = |t: &str| out.say(t);
    Some(match func {
        "autotrade" => crate::autotrade::atcommand_autotrade(c, &say, sd, message),
        "duel" => crate::duel::atcommand_duel(c, &say, sd, command, message),
        "invite" => crate::duel::atcommand_invite(c, &say, sd, command, message),
        "accept" => crate::duel::atcommand_accept(c, &say, sd),
        "reject" => crate::duel::atcommand_reject(c, &say, sd),
        _ => return None,
    })
}
