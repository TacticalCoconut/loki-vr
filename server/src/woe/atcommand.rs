//! map/atcommand.cpp's War of Emperium @commands: @agitstart / @agitend (FE), @agitstart2 / @agitend2 (SE),
//! @agitstart3 / @agitend3 (TE). atcommand::run routes the ACMD_FUNC names here.

use crate::data::msg_txt;
use crate::unit::Unit;
use crate::world::Ctx;

/// Calls the ACMD_FUNC `func` (replies go to `fd`, the invoker). None when `func` is not one of these.
pub fn run(c: &Ctx, fd: Option<&Unit>, _sd: u32, func: &str, _command: &str, _message: &str) -> Option<i32> {
    let (ok, msg_ok, msg_fail) = match func {
        "agitstart" => (super::guild_agit_start(c), 72, 73), // War of Emperium has been initiated. / is currently in progress.
        "agitstart2" => (super::guild_agit2_start(c), 403, 404), // War of Emperium SE has been initiated. / in progress.
        "agitstart3" => (super::guild_agit3_start(c), 749, 750), // War of Emperium TE has been initiated. / in progress.
        "agitend" => (super::guild_agit_end(c), 74, 75), // War of Emperium has been ended. / is currently not in progress.
        "agitend2" => (super::guild_agit2_end(c), 405, 406), // War of Emperium SE has been ended. / not in progress.
        "agitend3" => (super::guild_agit3_end(c), 751, 752), // War of Emperium TE has been ended. / not in progress.
        _ => return None,
    };
    let say = |n: u16| {
        if let Some(u) = fd {
            crate::clif::displaymessage(c, u, msg_txt(n));
        }
    };
    Some(if ok {
        say(msg_ok);
        0
    } else {
        say(msg_fail);
        -1
    })
}
