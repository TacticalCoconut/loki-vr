//! Administration, logging and string commands: logmes, kick, setbattleflag, get_revision, get_githash,
//! enable_command / disable_command, macro_detector, md5, escape_sql, query_sql / query_logsql, bindatcmd /
//! unbindatcmd / useatcmd, permission_check / permission_add / permission_remove. The `atcmd_binding` table
//! (pc_misc.rs) is kept here, with atcommand.rs's hooks for bound commands (get_atcommandbind_byname, npc_do_atcmd_event);
//! logmes writes log.rs's `npclog`.

use spacetimedb::Table;

use crate::npc::npc as _;
use crate::pc_misc::{atcmd_binding as _, AtcmdBinding};
use crate::script::npc_run::npc_event as _;
use crate::script::buildins::*;
use crate::script::npc_run;
use crate::script::vm::*;
use crate::unit;
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "logmes" => logmes,
        "kick" => kick,
        "setbattleflag" => setbattleflag,
        "get_revision" => get_revision,
        "get_githash" => get_githash,
        "enable_command" => enable_command,
        "disable_command" => disable_command,
        "macro_detector" => macro_detector,
        "md5" => md5,
        "escape_sql" => escape_sql,
        "query_sql" => query_sql,
        "query_logsql" => query_logsql,
        "bindatcmd" => bindatcmd,
        "unbindatcmd" => unbindatcmd,
        "useatcmd" => useatcmd,
        "permission_check" => permission_check,
        "permission_add" => permission_add,
        _ => return None,
    })
}

// ---------------------------------------------------------------------------------------------- logmes

/// safestrnlen(s, n) bytes of a Latin-1 string.
fn cut(s: &[u8], n: usize) -> String {
    lat(&s[..s.len().min(n)])
}

/// logmes "<message>": log_npc for the attached player, or the NPC when there is none (log.rs).
fn logmes(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let s = lat(&getstr(rt, st, 2));
    let sd = unit::get(c, st.rid).filter(|u| u.kind == crate::map::BL_PC);
    if let Some(sd) = sd {
        crate::map_log::log_npc_sd(c, sd.id, &s);
    } else {
        if c.db().npc().id().find(st.oid).is_none() {
            log::error!("buildin_logmes: Invalid usage without player or npc.");
            return SCRIPT_CMD_FAILURE;
        }
        crate::map_log::log_npc_nd(c, st.oid, &s);
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- kick

/// kick {<char id>}: clif_GM_kick(nullptr, sd) — SC_NOTIFY_BAN 15 and the connection closes (map_quit through
/// clif_quitsave). (sd->state.keepshop, which keeps a vending shop open over the quit, is the vending system's.)
fn kick(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_kick") else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    // clif_GM_kick(nullptr, tsd) (crate::session)
    crate::session::clif_gm_kick(c, None, aid);
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- battle config

/// setbattleflag "<flag>",<value>{,<reload>} (script.cpp:17011-17046): battle_set_value (the run-time battle_config,
/// battle_config/mod.rs). An unknown flag warns; a set one is logged and, when <reload> is given and not 0 and the
/// flag is one of the EXP / drop names of script.cpp:17024-17034, mob_reload runs.
fn setbattleflag(rt: &Rt, st: &mut State) -> i32 {
    let flag = lat(&getstr(rt, st, 2));
    let value = lat(&getstr(rt, st, 3)); // HACK: Retrieve number as string (auto-converted) for battle_set_value
    let Some(c) = rt.c else {
        log::warn!("buildin_setbattleflag: battle_config flag '{flag}' NOT set to '{value}': the script runs without a database");
        return SCRIPT_CMD_SUCCESS;
    };
    if crate::battle_config::battle_set_value(c, &flag, &value) == 0 {
        log::warn!("buildin_setbattleflag: unknown battle_config flag '{flag}'");
    } else {
        log::info!("buildin_setbattleflag: battle_config flag '{flag}' is now set to '{value}'.");
        // Only attempt to reload monster data if told to
        if st.hasdata(4) && getnum(rt, st, 4) != 0 {
            // Only certain flags require a reload, check for those types (the list is rAthena's, with its names that
            // no battle_data entry has)
            const EXPDROP_FLAGS: [&str; 41] = [
                "base_exp_rate", "job_exp_rate", "mvp_exp_rate", "quest_exp_rate", "heal_exp", "resurrection_exp",
                "item_rate_common", "item_rate_common_boss", "item_rate_common_mvp", "item_drop_common_min", "item_drop_common_max",
                "item_rate_heal", "item_rate_heal_boss", "item_rate_heal_mvp", "item_rate_heal_min", "item_rate_heal_max",
                "item_rate_use", "item_rate_use_boss", "item_rate_use_mvp", "item_rate_use_min", "item_rate_use_max",
                "item_rate_equip", "item_rate_equip_boss", "item_rate_equip_mvp", "item_rate_equip_min", "item_rate_equip_max",
                "item_rate_card", "item_rate_card_boss", "item_rate_card_mvp", "item_rate_card_min", "item_rate_card_max",
                "item_rate_mvp", "item_rate_mvp_min", "item_rate_mvp_max", "item_rate_mvp_mode",
                "item_rate_treasure", "item_rate_treasure_min", "item_rate_treasure_max",
                "item_logarithmic_drops", "drop_rate0item", "drop_rateincrease",
            ];
            if EXPDROP_FLAGS.iter().any(|f| f.eq_ignore_ascii_case(&flag)) {
                crate::atcommand_atcmd::admin::mob_reload(c);
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- version

/// get_revision(): the SVN revision, -1 when unknown (the module has no SVN / Git data; the reference oracle's build
/// has none either: "Cannot determine SVN/Git version.").
fn get_revision(_rt: &Rt, st: &mut State) -> i32 {
    st.pushint(-1);
    SCRIPT_CMD_SUCCESS
}

/// get_githash(): the Git hash, "Unknown" when unknown.
fn get_githash(_rt: &Rt, st: &mut State) -> i32 {
    st.pushstr(b"Unknown".to_vec());
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- commands

/// enable_command: sd->state.disable_atcommand_on_npc = 0 (commands allowed while talking to this NPC).
fn enable_command(rt: &Rt, st: &mut State) -> i32 {
    set_disable_atcommand(rt, st, "buildin_enable_command", false)
}

/// disable_command: sd->state.disable_atcommand_on_npc = 1.
fn disable_command(rt: &Rt, st: &mut State) -> i32 {
    set_disable_atcommand(rt, st, "buildin_disable_command", true)
}

fn set_disable_atcommand(rt: &Rt, st: &mut State, func: &str, v: bool) -> i32 {
    let Some(aid) = rid2sd(rt, st, func) else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    let mut x = npc_run::sdx(c, aid);
    x.disable_atcommand_on_npc = v;
    npc_run::put_sdx(c, &x);
    SCRIPT_CMD_SUCCESS
}

/// macro_detector {<account id>|"<char name>"}: pc_macro_reporter_process with the server as reporter (-1,
/// macro_detector.rs). It returns at once while captcha_db is empty (rAthena's db/captcha_db.yml lists none; captchas
/// come from /macro_register at run time).
fn macro_detector(rt: &Rt, st: &mut State) -> i32 {
    let aid = if st.hasdata(2) && isstring(rt, st, 2) {
        nick2sd(rt, st, 2, "buildin_macro_detector") // Character Name
    } else {
        accid2sd(rt, st, 2, "buildin_macro_detector") // Account ID
    };
    let Some(aid) = aid else {
        return SCRIPT_CMD_FAILURE;
    };
    // Reporter Account ID as -1 for server.
    crate::macro_detector::pc_macro_reporter_process(rt.c.unwrap(), aid, -1);
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- strings

/// md5("<string>"): md5calc.cpp MD5_String, 32 lowercase hex digits.
fn md5(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let d = md5_digest(&s);
    let hex: String = d.iter().map(|b| format!("{b:02x}")).collect();
    st.pushstr(hex.into_bytes());
    SCRIPT_CMD_SUCCESS
}

/// RFC 1321 MD5 (md5calc.cpp MD5_String2binary).
pub fn md5_digest(input: &[u8]) -> [u8; 16] {
    const S: [u32; 64] = [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
    ];
    let k: Vec<u32> = (0..64).map(|i| ((i as f64 + 1.0).sin().abs() * 4294967296.0) as u32).collect();
    let (mut a0, mut b0, mut c0, mut d0) = (0x67452301u32, 0xefcdab89u32, 0x98badcfeu32, 0x10325476u32);
    let mut msg = input.to_vec();
    let bit_len = (input.len() as u64).wrapping_mul(8);
    msg.push(0x80);
    while msg.len() % 64 != 56 {
        msg.push(0);
    }
    msg.extend_from_slice(&bit_len.to_le_bytes());
    for chunk in msg.chunks(64) {
        let m: Vec<u32> = chunk.chunks(4).map(|w| u32::from_le_bytes([w[0], w[1], w[2], w[3]])).collect();
        let (mut a, mut b, mut c, mut d) = (a0, b0, c0, d0);
        for i in 0..64 {
            let (f, g) = match i / 16 {
                0 => ((b & c) | (!b & d), i),
                1 => ((d & b) | (!d & c), (5 * i + 1) % 16),
                2 => (b ^ c ^ d, (3 * i + 5) % 16),
                _ => (c ^ (b | !d), (7 * i) % 16),
            };
            let f2 = f.wrapping_add(a).wrapping_add(k[i]).wrapping_add(m[g]);
            a = d;
            d = c;
            c = b;
            b = b.wrapping_add(f2.rotate_left(S[i]));
        }
        a0 = a0.wrapping_add(a);
        b0 = b0.wrapping_add(b);
        c0 = c0.wrapping_add(c);
        d0 = d0.wrapping_add(d);
    }
    let mut out = [0u8; 16];
    out[0..4].copy_from_slice(&a0.to_le_bytes());
    out[4..8].copy_from_slice(&b0.to_le_bytes());
    out[8..12].copy_from_slice(&c0.to_le_bytes());
    out[12..16].copy_from_slice(&d0.to_le_bytes());
    out
}

/// escape_sql("<string>"): Sql_EscapeStringLen = mysql_real_escape_string (single-byte connection character set):
/// NUL, \n, \r, \, ', " and Ctrl-Z get a backslash.
fn escape_sql(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    let mut out = Vec::with_capacity(s.len() * 2);
    for &b in &s {
        match b {
            0 => out.extend_from_slice(b"\\0"),
            b'\n' => out.extend_from_slice(b"\\n"),
            b'\r' => out.extend_from_slice(b"\\r"),
            b'\\' => out.extend_from_slice(b"\\\\"),
            b'\'' => out.extend_from_slice(b"\\'"),
            b'"' => out.extend_from_slice(b"\\\""),
            0x1a => out.extend_from_slice(b"\\Z"),
            _ => out.push(b),
        }
    }
    st.pushstr(out);
    SCRIPT_CMD_SUCCESS
}

/// query_logsql "<query>"{,<variable>…}: refused while the logs are not SQL (log_config.sql_logs; log.rs
/// log_config: yes by default), else buildin_query_sql_sub on the log database, which the module does not have either (the log
/// tables of log.rs are SpacetimeDB tables, not SQL a script could query): [`query_sql`]'s stub.
fn query_logsql(rt: &Rt, st: &mut State) -> i32 {
    if !rt.c.map(|c| crate::map_log::log_config(c).sql_logs).unwrap_or(crate::map_log::LOG_CONFIG.sql_logs) {
        // logmysql_handle == nullptr
        log::warn!("buildin_query_logsql: SQL logs are disabled, query '{}' will not be executed.", lat(&getstr(rt, st, 2)));
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    }
    query_sql(rt, st)
}

/// query_sql / query_logsql "<query>"{,<variable>…}: buildin_query_sql_sub's variable checks; the module has no SQL
/// database behind the scripts (stub: every query fails as an SQL error does: -1).
fn query_sql(rt: &Rt, st: &mut State) -> i32 {
    let func = st.funcname.clone();
    // check target variables
    let mut sd_found = false;
    let mut i = 3;
    while st.hasdata(i) {
        match st.data(i).clone() {
            Data::Name(id, _, _) => {
                let name = name_of(id);
                if not_server_variable(name.first().copied().unwrap_or(0)) && !sd_found {
                    // requires a player
                    if rid2sd(rt, st, &format!("buildin_{func}")).is_none() {
                        reportsrc(rt, st); // (script_reportdata)
                        st.state = END;
                        return SCRIPT_CMD_FAILURE;
                    }
                    sd_found = true;
                }
            }
            _ => {
                log::error!("script:query_sql: not a variable");
                reportsrc(rt, st);
                st.state = END;
                return SCRIPT_CMD_FAILURE;
            }
        }
        i += 1;
    }
    let query = lat(&getstr(rt, st, 2));
    log::error!("{func}: the module has no SQL database for scripts; query not run (stub): {query}");
    st.pushint(-1);
    SCRIPT_CMD_FAILURE
}

// ---------------------------------------------------------------------------------------------- atcommand bindings

/// atcmd_binding[] in array order.
pub(crate) fn bindings(c: &Ctx) -> Vec<AtcmdBinding> {
    let mut v: Vec<AtcmdBinding> = c.db().atcmd_binding().iter().collect();
    v.sort_by_key(|b| b.pos);
    v
}

/// The world is rebuilt (rAthena restarts): no bindings.
pub fn on_world_rebuilt(c: &Ctx) {
    for b in bindings(c) {
        c.db().atcmd_binding().pos().delete(b.pos);
    }
}

/// The command without its @ / # (bindatcmd, unbindatcmd, get_atcommandbind_byname: "for backwards compatibility").
fn strip_symbol(s: &[u8]) -> &[u8] {
    match s.first() {
        Some(&b) if b as char == crate::atcommand::ATCOMMAND_SYMBOL || b as char == crate::atcommand::CHARCOMMAND_SYMBOL => &s[1..],
        _ => s,
    }
}

/// bindatcmd "<command>","<NPC event>"{,<level>{,<level2>}}: binds (or rebinds) an @command to an NPC event.
fn bindatcmd(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let atcmd_full = getstr(rt, st, 2);
    let event = getstr(rt, st, 3);
    let atcmd = strip_symbol(&atcmd_full);
    let level = if st.hasdata(4) { getnum(rt, st, 4) } else { 0 };
    let level2 = if st.hasdata(5) { getnum(rt, st, 5) } else { 100 };
    let ev = cut(&event, npc_run::EVENT_NAME_LENGTH - 1);
    // ARR_FIND with strcmp: an existing entry is updated
    let all = bindings(c);
    if let Some(b) = all.iter().find(|b| b.command == lat(atcmd)) {
        c.db().atcmd_binding().pos().update(AtcmdBinding { npc_event: ev, level, level2, ..b.clone() });
    } else {
        let pos = all.last().map(|b| b.pos + 1).unwrap_or(0);
        c.db().atcmd_binding().insert(AtcmdBinding { pos, command: cut(atcmd, 49), npc_event: ev, level, level2 });
    }
    SCRIPT_CMD_SUCCESS
}

/// unbindatcmd "<command>": removes a binding (the list stays in order). Pushes 1 when it was bound.
fn unbindatcmd(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let atcmd_full = getstr(rt, st, 2);
    let atcmd = lat(strip_symbol(&atcmd_full));
    match bindings(c).into_iter().find(|b| b.command == atcmd) {
        Some(b) => {
            c.db().atcmd_binding().pos().delete(b.pos);
            st.pushint(1);
        }
        None => st.pushint(0), // not found
    }
    SCRIPT_CMD_SUCCESS
}

/// atcommand.cpp get_atcommandbind_byname: the binding of `name` (case-insensitive, with or without its symbol).
pub fn get_atcommandbind_byname(c: &Ctx, name: &str) -> Option<AtcmdBinding> {
    let n = lat(strip_symbol(&crate::net::wire::latin1(name)));
    bindings(c).into_iter().find(|b| b.command.eq_ignore_ascii_case(&n))
}

/// npc.cpp npc_do_atcmd_event: runs the bound event for `aid` with .@atcmd_command$, .@atcmd_parameters$[] (the
/// parameters split at spaces) and .@atcmd_numparameters; queued when the player is busy with an NPC.
pub fn npc_do_atcmd_event(c: &Ctx, aid: u32, command: &str, message: &str, eventname: &str) -> i32 {
    let Some(ev) = c.db().npc_event().name().find(&eventname.to_string()).filter(|e| c.db().npc().id().find(e.npc_id).is_some()) else {
        log::error!("npc_event: event not found [{eventname}]");
        return 0;
    };
    let Some(p) = crate::pc::get(c, aid) else { return 0 };
    if p.npc_id != 0 {
        // Enqueue the event trigger.
        let mut x = npc_run::sdx(c, aid);
        if let Some(l) = x.eventqueue.iter().position(|s| s.is_empty()) {
            x.eventqueue[l] = eventname.chars().take(npc_run::EVENT_NAME_LENGTH - 1).collect();
            npc_run::put_sdx(c, &x);
            return 0; // Event enqueued.
        }
        log::warn!("npc_event: player's event queue is full, can't add event '{eventname}' !");
        return 1;
    }
    let Some(nd) = c.db().npc().id().find(ev.npc_id) else { return 0 };
    if nd.is_invisible {
        // Disabled npc, shouldn't trigger event.
        npc_run::npc_event_dequeue(c, aid, true);
        return 2;
    }
    let Some((key, buf)) = crate::script::code::npc_code(c, ev.npc_id) else { return 0 };
    let rt = Rt::new(c);
    let mut st = State::new(key, buf, ev.pos, aid, ev.npc_id);
    crate::script::state::alloc(c, &mut st);
    let set_str = |st: &mut State, name: &[u8], idx: u32, v: &[u8]| {
        let id = add_str(name);
        crate::script::reg::set_reg_str(&rt, st, None, id, idx, name, v, &RegRef::None);
    };
    set_str(&mut st, b".@atcmd_command$", 0, &crate::net::wire::latin1(command));
    // split atcmd parameters based on spaces
    let msg = crate::net::wire::latin1(message);
    let mut temp: Vec<u8> = Vec::new();
    let mut j = 0u32;
    let mut i = 0usize;
    while i < msg.len() + 1 && temp.len() < 127 {
        let ch = msg.get(i).copied().unwrap_or(0);
        if ch == b' ' || ch == 0 {
            if i > 0 && msg[i - 1] == b' ' {
                i += 1;
                continue; // To prevent "@atcmd [space][space]" and .@atcmd_numparameters return 1 without any parameter.
            }
            if !temp.is_empty() {
                set_str(&mut st, b".@atcmd_parameters$", j, &temp);
                j += 1;
            }
            temp.clear();
        } else {
            temp.push(ch);
        }
        i += 1;
    }
    let id = add_str(b".@atcmd_numparameters");
    crate::script::reg::set_reg_num(&rt, &mut st, None, id, 0, b".@atcmd_numparameters", j as i64, &RegRef::None);
    run_script_main(&rt, st);
    0
}

/// useatcmd "<command>": atcommand_sub(st, 3) — the command as a player types it (bound commands included), without
/// the player-only checks.
fn useatcmd(rt: &Rt, st: &mut State) -> i32 {
    // atcommand_sub with type 3 (script.cpp:23096-23098): the attached player, or rAthena's dummy character
    crate::script::dummy_sd::atcommand_sub(rt, st, 3)
}

// ---------------------------------------------------------------------------------------------- permissions

/// pc_groups.hpp PC_PERM_MAX.
const PC_PERM_MAX: i32 = 31;

/// permission_check(<permission>{,<char id>}): pc_has_permission (sd->permissions).
fn permission_check(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_permission_check") else { return SCRIPT_CMD_FAILURE };
    let perm = getnum(rt, st, 2);
    if !(0..PC_PERM_MAX).contains(&perm) {
        log::error!("buildin_permission_check: Invalid permission {perm}");
        return SCRIPT_CMD_FAILURE;
    }
    st.pushint(crate::pc_misc::pc_has_permission(rt.c.unwrap(), aid, perm as u32) as i64);
    SCRIPT_CMD_SUCCESS
}

/// permission_add / permission_remove <permission>{,<char id>}: sets or clears one of the session's permissions
/// (until the player logs out; the group's permissions come back at the next login).
fn permission_add(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_permission_add") else { return SCRIPT_CMD_FAILURE };
    let perm = getnum(rt, st, 2);
    if !(0..PC_PERM_MAX).contains(&perm) {
        log::error!("buildin_permission_check: Invalid permission {perm}");
        return SCRIPT_CMD_FAILURE;
    }
    let add = st.funcname == "permission_add";
    let c = rt.c.unwrap();
    let mut m = crate::pc_misc::pcm(c, aid);
    let bit = 1u64 << perm;
    if add {
        m.perm_set |= bit; // Adds permission
        m.perm_reset &= !bit;
    } else {
        m.perm_reset |= bit; // Removes permission
        m.perm_set &= !bit;
    }
    crate::pc_misc::put_pcm(c, &m);
    SCRIPT_CMD_SUCCESS
}
