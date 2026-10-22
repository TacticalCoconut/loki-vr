//! atcommand.cpp's instance command: @reloadinstancedb (ACMD_FUNC(reloadinstancedb); @reload's "instancedb" type
//! calls the same function).

use crate::atcommand::Out;
use crate::data::msg_txt;
use crate::world::Ctx;

/// The instance commands, by ACMD_FUNC name (None: not one of them).
pub(crate) fn run(_c: &Ctx, out: &Out, _sd: u32, func: &str, _command: &str, _message: &str) -> Option<i32> {
    Some(match func {
        "reloadinstancedb" => reloadinstancedb(out),
        _ => return None,
    })
}

/// ACMD_FUNC(reloadinstancedb): `if (instance_db.reload()) clif_displaymessage(fd, msg_txt(sd,516));`. The database is
/// embedded in the module (db/misc/instance.rs), so reloading reads the same entries again and succeeds; running
/// instances keep their database id and are untouched, as in rAthena.
pub(crate) fn reloadinstancedb(out: &Out) -> i32 {
    out.say(msg_txt(516)); // Instance database has been reloaded.
    0
}
