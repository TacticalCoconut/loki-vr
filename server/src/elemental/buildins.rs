//! The elemental script commands of script.cpp: geteleminfo. (The getunitdata / setunitdata / getmapxy arms for
//! BL_ELEM are in the script2 unit commands, which call into elemental.rs.)

use super::data;
use crate::script::buildins::{charid2sd, BuildinFn};
use crate::script::vm::*;

/// script.hpp e_eleminfo_types.
pub const ELEMINFO_ID: i32 = 0;
pub const ELEMINFO_GAMEID: i32 = 1;
pub const ELEMINFO_CLASS: i32 = 2;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "geteleminfo" => geteleminfo,
        _ => return None,
    })
}

/// geteleminfo <type>{,<char_id>}: the character's elemental id, block id or class (0 without an elemental).
fn geteleminfo(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let Some(sd) = charid2sd(rt, st, 3, "geteleminfo") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let Some(ed) = data::of_master(c, sd) else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let type_ = getnum(rt, st, 2);
    match type_ {
        ELEMINFO_ID => st.pushint(ed.elemental.elemental_id as i64),
        ELEMINFO_GAMEID => st.pushint(ed.id as i64),
        ELEMINFO_CLASS => st.pushint(ed.elemental.class_ as i64),
        _ => {
            log::error!("buildin_geteleminfo: Invalid type '{type_}'.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
    }
    SCRIPT_CMD_SUCCESS
}
