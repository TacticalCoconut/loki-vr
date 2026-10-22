//! map/atcommand.cpp: the login / char server's account and character operations: @block (char_block), @ban,
//! @charban (char_ban), @unblock (char_unblock), @unban, @charunban (char_unban), @email, @vip, @changesex,
//! @changecharsex, @accinfo (atcmd agent, wave D). Routed from atcommand_atcmd.rs `run` by the ACMD_FUNC name.
//!
//! These commands only ask. The map server sends a request to the char server (map/chrif.cpp, map/intif.cpp), which
//! works on its tables and may forward it to the login server (char/char_mapif.cpp, char/inter.cpp →
//! login/loginchrif.cpp); the answers come back the same way (char/char_logif.cpp → map/chrif.cpp chrif_parse,
//! map/intif.cpp) in a later server loop, i.e. after the command has returned and its own replies were written.
//!
//! Deliberately different (no processes, no sockets, no SQL): the three servers' parts are ported here as functions
//! named after rAthena's, and every packet on those links is an [`AccInter`] message: queued by [`send`] (WFIFOSET,
//! crate::intif's queue) and handled in order by the receiving server's function ([`dispatch`]) when the reducer's
//! work is done (world::run drains the queue after clif_parse's eof pass). The char server's `char` table is `character` (character.rs; `unban_time` is there),
//! the login server's `login` table is `account` (account.rs: userid, sex, group_id, last login) plus the columns it
//! lacks in [`LoginExt`] (this file). rAthena's SQL look-ups by name are MySQL comparisons under the tables'
//! case-insensitive PAD SPACE collation (see [`char_by_name`], [`sql_like`]).
//!
//! The kick of a player who is online when blocked / banned comes back from the login and char servers as 0x2b14
//! (chlogif_parse_accbannotification / chmapif_parse_reqcharban → map/chrif.cpp chrif_ban: a message, set_eof,
//! map_quit), not through chrif_disconnectplayer (that one is the char server's double-login kick). Refusing later
//! logins (login_mmo_auth's state / unban_time checks) and the char-banned character at the character screen are
//! account.rs / character.rs work: [`login_mmo_auth_check`], [`login_mmo_auth_accepted`] and [`char_ban_active`] are
//! the functions those files call.

use spacetimedb::{table, Identity, Table, Timestamp};

use crate::account::{account, session};
use crate::atcommand_atcmd::Out;
use crate::character::{character, Character};
use crate::cstr::Scan;
use crate::data::msg_txt;
use crate::net::wire::{from_latin1, latin1};
use crate::script::npc_run::{localtime, Tm, LOCAL_TZ_OFFSET_SECS};
use crate::unit::Unit;
use crate::world::Ctx;

/// mmo.hpp NAME_LENGTH (23 + 1).
const NAME_LENGTH: usize = 24;
/// mmo.hpp START_ACCOUNT_NUM.
const START_ACCOUNT_NUM: u32 = crate::rules::START_ACCOUNT_ID;
/// mmo.hpp MAX_CHARS (PACKETVER >= 20180124).
const MAX_CHARS: usize = crate::rules::CHAR_SLOTS as usize;
/// mmo.hpp e_sex.
const SEX_FEMALE: u8 = 0;
const SEX_MALE: u8 = 1;

/// common/socket.hpp enum chrif_req_op (char-server <-> login-server operations, then char-server operations).
pub const CHRIF_OP_LOGIN_BLOCK: u16 = 1;
pub const CHRIF_OP_LOGIN_BAN: u16 = 2;
pub const CHRIF_OP_LOGIN_UNBLOCK: u16 = 3;
pub const CHRIF_OP_LOGIN_UNBAN: u16 = 4;
pub const CHRIF_OP_LOGIN_CHANGESEX: u16 = 5;
pub const CHRIF_OP_LOGIN_VIP: u16 = 6;
pub const CHRIF_OP_CHANGECHARSEX: u16 = 9;

/// Runs `func` when it is one of this file's commands; None otherwise.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    let r = match func {
        "char_block" => char_block(c, &out, sd, command, message),
        "ban" => ban(c, &out, sd, command, message),
        "char_ban" => char_ban(c, &out, sd, command, message),
        "char_unblock" => char_unblock(c, &out, sd, command, message),
        "unban" => unban(c, &out, sd, command, message),
        "char_unban" => char_unban(c, &out, sd, command, message),
        "email" => email(c, &out, sd, message),
        "vip" => vip(&out),
        "changesex" => changesex(c, sd, true),
        "changecharsex" => changesex(c, sd, false),
        "accinfo" => accinfo(c, &out, sd, message),
        _ => return None,
    };
    Some(r)
}

// ---------------------------------------------------------------------------------------------- the login table

/// rAthena's `login` columns (sql-files/main.sql) that the module's `account` table lacks, for the login server's
/// part of these commands (login/loginchrif.cpp) and login_mmo_auth's checks. Keyed by account id; an account without
/// a row has the values login_mmo_auth_new gives a new account (login.cpp:256-260: email "a@a.com", state 0,
/// unban_time 0, logincount 0, birthdate ""). Not kept: `last_ip` (the module never sees the client's address),
/// `birthdate` (nothing sets it), `vip_time` / `old_group` (VIP_ENABLE is off in config/core.hpp: the login server
/// never reads or writes them).
#[table(accessor = login_ext)]
#[derive(Clone, Debug)]
pub struct LoginExt {
    #[primary_key]
    pub account_id: u32,
    /// `email` varchar(39).
    pub email: String,
    /// `state`: 0 normal, 5 = blocked (@block); login_mmo_auth refuses any other value with code state - 1.
    pub state: u32,
    /// `unban_time` int(11) unsigned: the end of an @ban (unix seconds), 0 = none.
    pub unban_time: u32,
    /// `logincount` mediumint(9) unsigned.
    pub logincount: u32,
    /// `expiration_time` int(11) unsigned: the account may not log in after it (unix seconds), 0 = no limit (wave I,
    /// ionline). login_mmo_auth_new gives a new account 0 (`start_limited_time: -1`, conf/login_athena.conf); no
    /// command changes it: on rAthena an administrator writes the column, here the database's owner does
    /// (`UPDATE login_ext SET expiration_time = … WHERE account_id = …`). Only the login refusal is ported
    /// ([`login_mmo_auth_check`]); the map server's side of it (pc.cpp pc_check_expiration's notice and
    /// pc_expiration_timer's kick of a player in the world) is not: inventory.rs `pc_check_expiration` stays a stub.
    pub expiration_time: u32,
}

/// AccountDB load of the `login` columns in [`LoginExt`] (the defaults when the account has no row).
fn login_ext_get(db: &spacetimedb::Local, account_id: u32) -> LoginExt {
    db.login_ext().account_id().find(account_id).unwrap_or(LoginExt { account_id, email: "a@a.com".into(), state: 0, unban_time: 0, logincount: 0, expiration_time: 0 })
}

/// AccountDB save of the `login` columns in [`LoginExt`].
fn login_ext_put(db: &spacetimedb::Local, r: LoginExt) {
    if db.login_ext().account_id().find(r.account_id).is_some() {
        db.login_ext().account_id().update(r);
    } else {
        db.login_ext().insert(r);
    }
}

/// time(nullptr) of a reducer timestamp.
fn secs_of(t: Timestamp) -> i64 {
    t.to_micros_since_unix_epoch().div_euclid(1_000_000)
}

/// time(nullptr) for this call.
fn now_secs(c: &Ctx) -> i64 {
    c.tick().div_euclid(1000)
}

/// login.cpp login_mmo_auth's account checks after the password (login.cpp:365-375), for account.rs's `login`
/// reducer and character.rs's character screen (the module's login binding outlives rAthena's per-connection
/// login_mmo_auth). Some(the refusal text) when the account may not log in:
/// - `expiration_time` passed: refusal 2 "This ID is expired" (login.cpp:358-361, before the ban test);
/// - `unban_time` still to come: refusal 6 "Your are Prohibited to log in until %s" (login_config.date_format
///   "%Y-%m-%d %H:%M:%S", conf/login_athena.conf, server time zone);
/// - `state` != 0: refusal `state - 1`, whose client text is the map_msg list chrif_ban shows for that state (411 +
///   state: 5 = "Your account has been blocked by the GM Team.").
pub fn login_mmo_auth_check(db: &spacetimedb::Local, account_id: u32, now: Timestamp) -> Option<String> {
    let r = db.login_ext().account_id().find(account_id)?;
    let now = secs_of(now);
    // `if( acc.expiration_time != 0 && acc.expiration_time < time(nullptr) ) return 2; // 2 = This ID is expired`
    if expired(r.expiration_time, now) {
        log::info!("Connection refused (account: {account_id}, expired ID)");
        return Some(crate::account::refuse_login(2, "Account Expired.")); // conf/msg_conf/login_msg.conf 2
    }
    if r.unban_time != 0 && r.unban_time as i64 > now {
        let s = crate::script::cfmt::strftime(b"%Y-%m-%d %H:%M:%S", 24, &localtime(r.unban_time as i64));
        log::info!("Connection refused (account: {account_id}, banned until {})", from_latin1(&s));
        // `return 6;` then logclif_auth_failed fills the packet's unblock_time with the same date (loginclif.cpp:224-231)
        let until = from_latin1(&s);
        return Some(crate::account::refuse_login_until(&until, &format!("You are prohibited to log in until {until}.")));
    }
    if r.state != 0 {
        log::info!("Connection refused (account: {account_id}, state: {})", r.state);
        // `return acc.state - 1;` (login.cpp:369-372)
        return Some(crate::account::refuse_login(r.state.wrapping_sub(1), msg_txt(state_msg(r.state as i32))));
    }
    None
}

/// login.cpp login_mmo_auth:358: `acc.expiration_time != 0 && acc.expiration_time < time(nullptr)`.
pub fn expired(expiration_time: u32, now_secs: i64) -> bool {
    expiration_time != 0 && (expiration_time as i64) < now_secs
}

/// login.cpp login_mmo_auth:419-424 when the login is accepted: `acc.unban_time = 0; acc.logincount++;` then saved
/// (last_ip: none here; lastlogin is `Account::last_login`, written by account.rs). The _M / _F registration
/// (login_mmo_auth_new) goes on through login_mmo_auth, so a new account has logged in once.
pub fn login_mmo_auth_accepted(db: &spacetimedb::Local, account_id: u32) {
    let mut r = login_ext_get(db, account_id);
    r.unban_time = 0;
    // mediumint(9) unsigned column: MySQL keeps it at its maximum
    r.logincount = (r.logincount + 1).min(16_777_215);
    login_ext_put(db, r);
}

/// char_clif.cpp chclif_parse_charselect:1109-1113 (and chclif_parse_select_accessible_map:949-953), for
/// character.rs's character selection: the character is banned (@charban) until a time still to come, so the
/// selection is rejected (chclif_reject 0, "rejected from server").
pub fn char_ban_active(ch: &Character, now: Timestamp) -> bool {
    ch.unban_time as i64 > secs_of(now)
}

/// The map_msg number of chrif_ban's message for an account state (411 + state for 1..9, 421 for 100, else 420).
fn state_msg(state: i32) -> u16 {
    if 0 < state && state <= 9 {
        (411 + state) as u16
    } else if state == 100 {
        421
    } else {
        420
    }
}

// ---------------------------------------------------------------------------------------------- the server links

/// A login server account record as logchrif_parse_accinfo sends it (0x2721).
#[derive(Clone, Debug)]
pub struct AccInfo {
    pub group_id: i32,
    pub logincount: i32,
    pub state: i32,
    pub email: Vec<u8>,
    pub last_ip: Vec<u8>,
    pub lastlogin: Vec<u8>,
    pub birthdate: Vec<u8>,
    pub userid: Vec<u8>,
}

/// One packet on the map ↔ char ↔ login server links, for these commands. `u_fd` is the player's session (sd->fd;
/// None = fd 0), names are latin-1 bytes.
#[derive(Clone, Debug)]
pub enum AccInter {
    // ---- map → char (map/chrif.cpp, map/intif.cpp → char/char_mapif.cpp, char/inter.cpp)
    /// 0x2b0e chrif_req_login_operation / chrif_changesex → chmapif_parse_fwlog_changestatus.
    LoginOperation { aid: i32, name: Vec<u8>, op: u16, timediff: i32, val1: i32, sex: u8 },
    /// 0x2b0c chrif_changeemail → chmapif_parse_reqnewemail.
    ChangeEmail { account_id: u32, actual: Vec<u8>, new: Vec<u8> },
    /// 0x2b28 chrif_req_charban → chmapif_parse_reqcharban.
    CharBan { aid: u32, timediff: i32, name: Vec<u8> },
    /// 0x2b2a chrif_req_charunban → chmapif_parse_reqcharunban.
    CharUnban { aid: u32, name: Vec<u8> },
    /// 0x3007 intif_request_accinfo → mapif_parse_accinfo.
    AccInfoReq { u_fd: Option<Identity>, u_aid: u32, group_lv: i32, query: Vec<u8> },
    // ---- char → login (char/char_mapif.cpp, char/inter.cpp → login/loginchrif.cpp)
    /// 0x2722 → logchrif_parse_reqchangemail.
    LoginChangeEmail { account_id: u32, actual: Vec<u8>, new: Vec<u8> },
    /// 0x2724 → logchrif_parse_requpdaccstate.
    LoginUpdAccState { account_id: u32, state: u32 },
    /// 0x2725 → logchrif_parse_reqbanacc.
    LoginBanAcc { account_id: u32, timediff: i32 },
    /// 0x2727 → logchrif_parse_reqchgsex.
    LoginChgSex { account_id: u32 },
    /// 0x272a → logchrif_parse_requnbanacc.
    LoginUnbanAcc { account_id: u32 },
    /// 0x2720 chlogif_req_accinfo → logchrif_parse_accinfo.
    LoginAccInfo { u_fd: Option<Identity>, u_aid: u32, account_id: i32 },
    // ---- login → char (login/loginchrif.cpp → char/char_logif.cpp)
    /// 0x2731 → chlogif_parse_accbannotification (kind 0: change of state, 1: ban).
    AccBanNotification { account_id: u32, kind: u8, value: u32 },
    /// 0x2723 → chlogif_parse_ackchangesex.
    AckChangeSex { account_id: u32, sex: u8 },
    /// 0x2721 → chlogif_parse_AccInfoAck (info None: failed).
    AccInfoAck { u_fd: Option<Identity>, u_aid: u32, account_id: i32, info: Option<Box<AccInfo>> },
    // ---- char → map (char/*.cpp → map/chrif.cpp chrif_parse, map/intif.cpp intif_parse)
    /// 0x2b0f → chrif_ack_login_req.
    AckLoginReq { aid: i32, name: Vec<u8>, op: u16, answer: u16 },
    /// 0x2b0d → chrif_changedsex.
    ChangedSex { account_id: u32, sex: u8 },
    /// 0x2b14 → chrif_ban (res 0: change of state, 1: ban, 2: charban — `id` is then a char id).
    Ban { id: u32, res: u8, value: u32 },
    /// 0x3807 inter_to_fd → intif_parse_MessageToFD.
    MessageToFd { u_fd: Option<Identity>, aid: u32, msg: Vec<u8> },
}

/// WFIFOSET on one of the links: crate::intif's queue, which the reducer drains after the call's work and after
/// clif_parse's eof pass (world::run) — the receiving server reads it in a later server loop, as in rAthena.
fn send(_c: &Ctx, m: AccInter) {
    crate::intif::send(crate::intif::Inter::Accounts(m));
}

/// The receiving server's parse function for each message (chmapif_parse / inter_parse_frommap, logchrif_parse,
/// chlogif_parse, chrif_parse / intif_parse).
pub fn dispatch(c: &Ctx, m: AccInter) {
    match m {
        AccInter::LoginOperation { aid, name, op, timediff, val1, sex } => chmapif_parse_fwlog_changestatus(c, aid, &name, op, timediff, val1, sex),
        AccInter::ChangeEmail { account_id, actual, new } => chmapif_parse_reqnewemail(c, account_id, actual, new),
        AccInter::CharBan { aid, timediff, name } => chmapif_parse_reqcharban(c, aid, timediff, &name),
        AccInter::CharUnban { aid, name } => chmapif_parse_reqcharunban(c, aid, &name),
        AccInter::AccInfoReq { u_fd, u_aid, group_lv, query } => mapif_parse_accinfo(c, u_fd, u_aid, group_lv, &query),
        AccInter::LoginChangeEmail { account_id, actual, new } => logchrif_parse_reqchangemail(c, account_id, &actual, &new),
        AccInter::LoginUpdAccState { account_id, state } => logchrif_parse_requpdaccstate(c, account_id, state),
        AccInter::LoginBanAcc { account_id, timediff } => logchrif_parse_reqbanacc(c, account_id, timediff),
        AccInter::LoginChgSex { account_id } => logchrif_parse_reqchgsex(c, account_id),
        AccInter::LoginUnbanAcc { account_id } => logchrif_parse_requnbanacc(c, account_id),
        AccInter::LoginAccInfo { u_fd, u_aid, account_id } => logchrif_parse_accinfo(c, u_fd, u_aid, account_id),
        AccInter::AccBanNotification { account_id, kind, value } => chlogif_parse_accbannotification(c, account_id, kind, value),
        AccInter::AckChangeSex { account_id, sex } => chlogif_parse_ackchangesex(c, account_id, sex),
        AccInter::AccInfoAck { u_fd, u_aid, account_id, info } => mapif_accinfo_ack(c, u_fd, u_aid, account_id, info.as_deref()),
        AccInter::AckLoginReq { aid, name, op, answer } => chrif_ack_login_req(c, aid, &name, op, answer),
        AccInter::ChangedSex { account_id, sex } => {
            chrif_changedsex(c, account_id, sex);
        }
        AccInter::Ban { id, res, value } => {
            chrif_ban(c, id, res, value);
        }
        AccInter::MessageToFd { u_fd, aid, msg } => intif_parse_message_to_fd(c, u_fd, aid, &msg),
    }
}

// ---------------------------------------------------------------------------------------------- C helpers

/// One C printf argument.
enum A<'a> {
    S(&'a [u8]),
    D(i64),
}

fn int_arg(args: &[A], ai: &mut usize) -> i64 {
    let v = match args.get(*ai) {
        Some(A::D(v)) => *v,
        _ => 0,
    };
    *ai += 1;
    v
}

fn str_arg<'a>(args: &'a [A<'a>], ai: &mut usize) -> &'a [u8] {
    let v: &[u8] = match args.get(*ai) {
        Some(A::S(s)) => s,
        _ => b"",
    };
    *ai += 1;
    v
}

/// C sprintf for the message formats these commands print (%s %d %i %u %c %%, the flags '-' and '0', width and
/// precision as digits or `*` — msg 424's `%.*s` —, length modifiers skipped), on latin-1 bytes; a string argument
/// ends at its first NUL.
fn csprintf(fmt: &str, args: &[A]) -> String {
    let f = latin1(fmt);
    let mut out: Vec<u8> = Vec::new();
    let (mut ai, mut i) = (0usize, 0usize);
    while i < f.len() {
        if f[i] != b'%' {
            out.push(f[i]);
            i += 1;
            continue;
        }
        i += 1;
        if i >= f.len() {
            break;
        }
        if f[i] == b'%' {
            out.push(b'%');
            i += 1;
            continue;
        }
        let (mut left, mut zero) = (false, false);
        while i < f.len() && matches!(f[i], b'-' | b'0' | b'+' | b' ' | b'#') {
            match f[i] {
                b'-' => left = true,
                b'0' => zero = true,
                _ => {}
            }
            i += 1;
        }
        let mut width = 0usize;
        if i < f.len() && f[i] == b'*' {
            let w = int_arg(args, &mut ai) as i32;
            left |= w < 0;
            width = w.unsigned_abs() as usize;
            i += 1;
        } else {
            while i < f.len() && f[i].is_ascii_digit() {
                width = width * 10 + (f[i] - b'0') as usize;
                i += 1;
            }
        }
        let mut prec: Option<usize> = None;
        if i < f.len() && f[i] == b'.' {
            i += 1;
            if i < f.len() && f[i] == b'*' {
                let p = int_arg(args, &mut ai) as i32;
                prec = if p < 0 { None } else { Some(p as usize) };
                i += 1;
            } else {
                let mut p = 0usize;
                while i < f.len() && f[i].is_ascii_digit() {
                    p = p * 10 + (f[i] - b'0') as usize;
                    i += 1;
                }
                prec = Some(p);
            }
        }
        while i < f.len() && matches!(f[i], b'h' | b'l' | b'L' | b'q' | b'j' | b'z' | b't') {
            i += 1;
        }
        if i >= f.len() {
            break;
        }
        let conv = f[i];
        i += 1;
        let numeric = matches!(conv, b'd' | b'i' | b'u');
        let body: Vec<u8> = match conv {
            b'd' | b'i' => (int_arg(args, &mut ai) as i32).to_string().into_bytes(),
            b'u' => (int_arg(args, &mut ai) as u32).to_string().into_bytes(),
            b'c' => vec![int_arg(args, &mut ai) as u8],
            b's' => {
                let s = str_arg(args, &mut ai);
                let s = &s[..s.iter().position(|&b| b == 0).unwrap_or(s.len())];
                match prec {
                    Some(p) if p < s.len() => s[..p].to_vec(),
                    _ => s.to_vec(),
                }
            }
            _ => Vec::new(),
        };
        if body.len() >= width {
            out.extend_from_slice(&body);
        } else if left {
            out.extend_from_slice(&body);
            out.resize(out.len() + width - body.len(), b' ');
        } else if zero && numeric {
            let (sign, digits) = if body.first() == Some(&b'-') { body.split_at(1) } else { body.split_at(0) };
            out.extend_from_slice(sign);
            out.resize(out.len() + width - body.len(), b'0');
            out.extend_from_slice(digits);
        } else {
            out.resize(out.len() + width - body.len(), b' ');
            out.extend_from_slice(&body);
        }
    }
    from_latin1(&out)
}

/// safestrncpy(dst, src, NAME_LENGTH): at most 23 bytes, up to the first NUL.
fn name24(b: &[u8]) -> Vec<u8> {
    let end = b.iter().position(|&x| x == 0).unwrap_or(b.len()).min(NAME_LENGTH - 1);
    b[..end].to_vec()
}

/// strcmpi (strcasecmp in the C locale: only ASCII letters fold) == 0.
fn strcmpi_eq(a: &[u8], b: &[u8]) -> bool {
    a.eq_ignore_ascii_case(b)
}

/// common/strlib.cpp e_mail_check: 1 when the address looks valid.
fn e_mail_check(email: &[u8]) -> bool {
    let email = &email[..email.iter().position(|&b| b == 0).unwrap_or(email.len())];
    let len = email.len();
    // athena limits
    if !(3..=39).contains(&len) {
        return false;
    }
    // part of RFC limits (official reference of e-mail description)
    if !email.contains(&b'@') || email[len - 1] == b'@' {
        return false;
    }
    if email[len - 1] == b'.' {
        return false;
    }
    let at = email.iter().rposition(|&b| b == b'@').unwrap_or(0);
    let last_arobas = &email[at..];
    if last_arobas.windows(2).any(|w| w == b"@." || w == b"..") {
        return false;
    }
    if last_arobas.iter().any(|&b| (1..32).contains(&b)) {
        return false;
    }
    if last_arobas.contains(&b' ') || last_arobas.contains(&b';') {
        return false;
    }
    // all correct
    true
}

/// Howard Hinnant's days_from_civil: days since 1970-01-01 of a proleptic Gregorian date (month 1..12).
fn days_from_civil(y: i64, m: i64, d: i64) -> i64 {
    let y = if m <= 2 { y - 1 } else { y };
    let era = y.div_euclid(400);
    let yoe = y - era * 400;
    let doy = (153 * ((m + 9) % 12) + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146_097 + doe - 719_468
}

/// time.h mktime for the server's local zone (script/npc_run.rs LOCAL_TZ_OFFSET_SECS: the oracle's
/// America/Sao_Paulo, UTC-3 without daylight saving since 2019; earlier dates had summer time there, which this
/// fixed offset does not model). Fields out of range carry over as glibc's __mktime_internal does: the month into the
/// year, then days, hours, minutes and seconds linearly.
fn mktime(tm: &Tm) -> i64 {
    let mon = tm.mon as i64;
    let year = tm.year as i64 + 1900 + mon.div_euclid(12);
    let days = days_from_civil(year, mon.rem_euclid(12) + 1, 1) + tm.mday as i64 - 1;
    days * 86_400 + tm.hour as i64 * 3600 + tm.min as i64 * 60 + tm.sec as i64 - LOCAL_TZ_OFFSET_SECS
}

/// common/timer.cpp solve_time at time `now`: the seconds between now and now altered by "+/-<value><unit>" pieces
/// (s, n / mn, h, d / j, m (month), y / a) applied to the local calendar fields, then mktime; a piece whose atoi value
/// is 0 skips one character, an unknown unit drops its value.
fn solve_time_at(now: i64, modif: &[u8]) -> f64 {
    let mut tm = localtime(now);
    let at = |i: usize| modif.get(i).copied().unwrap_or(0);
    let mut p = 0usize;
    while at(p) != 0 {
        let value = crate::cstr::atoi(&from_latin1(&modif[p..]));
        if value == 0 {
            p += 1;
            continue;
        }
        if at(p) == b'-' || at(p) == b'+' {
            p += 1;
        }
        while at(p).is_ascii_digit() {
            p += 1;
        }
        // the int fields of struct tm (an overflow is undefined in C: wrapping here)
        match at(p) {
            b's' => {
                tm.sec = tm.sec.wrapping_add(value);
                p += 1;
            }
            b'n' => {
                tm.min = tm.min.wrapping_add(value);
                p += 1;
            }
            b'm' if at(p + 1) == b'n' => {
                tm.min = tm.min.wrapping_add(value);
                p += 2;
            }
            b'h' => {
                tm.hour = tm.hour.wrapping_add(value);
                p += 1;
            }
            b'd' | b'j' => {
                tm.mday = tm.mday.wrapping_add(value);
                p += 1;
            }
            b'm' => {
                tm.mon = tm.mon.wrapping_add(value);
                p += 1;
            }
            b'y' | b'a' => {
                tm.year = tm.year.wrapping_add(value);
                p += 1;
            }
            0 => {}
            _ => p += 1,
        }
    }
    let then = mktime(&tm);
    (then - now) as f64 // difftime(then, now)
}

/// `(int32)solve_time(modif_p)` in the ban / charban commands (the double → int32 cast of x86-64: out of range gives
/// INT_MIN, i.e. a negative time).
pub(crate) fn solve_time(c: &Ctx, modif: &[u8]) -> i32 {
    crate::atcommand_atcmd::c_double_to_i32(solve_time_at(now_secs(c), modif))
}

/// The char server's `SELECT … FROM char WHERE name = '<name>'` (strnlen(name, NAME_LENGTH), escaped): MySQL
/// compares under the table's case-insensitive PAD SPACE collation, so the case and trailing spaces do not count. The
/// module keeps the lower-case copy `name_key` for this (character.rs); the accent folding of *_general_ci
/// collations is not modelled.
fn char_by_name(c: &Ctx, name: &[u8]) -> Option<Character> {
    let mut n = name[..name.iter().position(|&b| b == 0).unwrap_or(name.len()).min(NAME_LENGTH)].to_vec();
    while n.last() == Some(&b' ') {
        n.pop();
    }
    c.db().character().name_key().find(from_latin1(&n).to_lowercase())
}

/// A latin-1 byte under a case-insensitive collation (the module's `to_lowercase`, as `name_key`).
fn fold(b: u8) -> char {
    (b as char).to_lowercase().next().unwrap_or(b as char)
}

/// MySQL `text LIKE 'pattern'` (the pattern as the SQL string literal holds it after Sql_EscapeString and the parser
/// undid each other: the query as typed) under a case-insensitive collation: `%` any run of characters, `_` one
/// character, `\` makes the next character literal (a trailing `\` is itself). Trailing spaces count.
fn sql_like(text: &[u8], pat: &[u8]) -> bool {
    enum T {
        Any,
        One,
        Lit(char),
    }
    let mut toks = Vec::new();
    let mut i = 0;
    while i < pat.len() {
        match pat[i] {
            b'%' => toks.push(T::Any),
            b'_' => toks.push(T::One),
            b'\\' if i + 1 < pat.len() => {
                i += 1;
                toks.push(T::Lit(fold(pat[i])));
            }
            b => toks.push(T::Lit(fold(b))),
        }
        i += 1;
    }
    let t: Vec<char> = text.iter().map(|&b| fold(b)).collect();
    // dp[j] = the first i tokens match t[..j]
    let mut dp = vec![false; t.len() + 1];
    dp[0] = true;
    for tok in &toks {
        let mut nd = vec![false; t.len() + 1];
        match tok {
            T::Any => {
                let mut any = false;
                for j in 0..=t.len() {
                    any |= dp[j];
                    nd[j] = any;
                }
            }
            T::One => {
                for j in 1..=t.len() {
                    nd[j] = dp[j - 1];
                }
            }
            T::Lit(ch) => {
                for j in 1..=t.len() {
                    nd[j] = dp[j - 1] && t[j - 1] == *ch;
                }
            }
        }
        dp = nd;
    }
    dp[t.len()]
}

/// char/inter.cpp job_name ("from pc.cpp due to @accinfo"): the class name from char_msg.conf (the char server's
/// messages, not map_msg's). rAthena quirks kept: Oktoberfest reads 105 - JOB_HANBOK + class_ = 106 "Rebellion"
/// (char_msg 107 is "Oktoberfest"), and the Star Emperor / Soul Reaper group is numbered from 114, so Star
/// Emperor prints "Baby Soul Linker", Soul Reaper "Baby Gunslinger", Baby Star Emperor "Baby Rebellion" and Baby Soul
/// Reaper "Star Emperor".
fn char_job_name(class_: i32) -> &'static str {
    use crate::db::jobs::ids::*;
    let n = match class_ {
        JOB_NOVICE | JOB_SWORDMAN | JOB_MAGE | JOB_ARCHER | JOB_ACOLYTE | JOB_MERCHANT | JOB_THIEF => JOB_NOVICE + class_,
        JOB_KNIGHT | JOB_PRIEST | JOB_WIZARD | JOB_BLACKSMITH | JOB_HUNTER | JOB_ASSASSIN => 7 - JOB_KNIGHT + class_,
        JOB_KNIGHT2 => 7,
        JOB_CRUSADER | JOB_MONK | JOB_SAGE | JOB_ROGUE | JOB_ALCHEMIST | JOB_BARD | JOB_DANCER => 13 - JOB_CRUSADER + class_,
        JOB_CRUSADER2 => 13,
        JOB_WEDDING | JOB_SUPER_NOVICE | JOB_GUNSLINGER | JOB_NINJA | JOB_XMAS => 20 - JOB_WEDDING + class_,
        JOB_SUMMER | JOB_SUMMER2 => 71,
        JOB_HANBOK | JOB_OKTOBERFEST => 105 - JOB_HANBOK + class_,
        JOB_NOVICE_HIGH | JOB_SWORDMAN_HIGH | JOB_MAGE_HIGH | JOB_ARCHER_HIGH | JOB_ACOLYTE_HIGH | JOB_MERCHANT_HIGH | JOB_THIEF_HIGH => 25 - JOB_NOVICE_HIGH + class_,
        JOB_LORD_KNIGHT | JOB_HIGH_PRIEST | JOB_HIGH_WIZARD | JOB_WHITESMITH | JOB_SNIPER | JOB_ASSASSIN_CROSS => 32 - JOB_LORD_KNIGHT + class_,
        JOB_LORD_KNIGHT2 => 32,
        JOB_PALADIN | JOB_CHAMPION | JOB_PROFESSOR | JOB_STALKER | JOB_CREATOR | JOB_CLOWN | JOB_GYPSY => 38 - JOB_PALADIN + class_,
        JOB_PALADIN2 => 38,
        JOB_BABY | JOB_BABY_SWORDMAN | JOB_BABY_MAGE | JOB_BABY_ARCHER | JOB_BABY_ACOLYTE | JOB_BABY_MERCHANT | JOB_BABY_THIEF => 45 - JOB_BABY + class_,
        JOB_BABY_KNIGHT | JOB_BABY_PRIEST | JOB_BABY_WIZARD | JOB_BABY_BLACKSMITH | JOB_BABY_HUNTER | JOB_BABY_ASSASSIN => 52 - JOB_BABY_KNIGHT + class_,
        JOB_BABY_KNIGHT2 => 52,
        JOB_BABY_CRUSADER | JOB_BABY_MONK | JOB_BABY_SAGE | JOB_BABY_ROGUE | JOB_BABY_ALCHEMIST | JOB_BABY_BARD | JOB_BABY_DANCER => 58 - JOB_BABY_CRUSADER + class_,
        JOB_BABY_CRUSADER2 => 58,
        JOB_SUPER_BABY => 65,
        JOB_TAEKWON => 66,
        JOB_STAR_GLADIATOR | JOB_STAR_GLADIATOR2 => 67,
        JOB_SOUL_LINKER => 68,
        JOB_GANGSI | JOB_DEATH_KNIGHT | JOB_DARK_COLLECTOR => 72 - JOB_GANGSI + class_,
        JOB_RUNE_KNIGHT | JOB_WARLOCK | JOB_RANGER | JOB_ARCH_BISHOP | JOB_MECHANIC | JOB_GUILLOTINE_CROSS => 75 - JOB_RUNE_KNIGHT + class_,
        JOB_RUNE_KNIGHT_T | JOB_WARLOCK_T | JOB_RANGER_T | JOB_ARCH_BISHOP_T | JOB_MECHANIC_T | JOB_GUILLOTINE_CROSS_T => 75 - JOB_RUNE_KNIGHT_T + class_,
        JOB_ROYAL_GUARD | JOB_SORCERER | JOB_MINSTREL | JOB_WANDERER | JOB_SURA | JOB_GENETIC | JOB_SHADOW_CHASER => 81 - JOB_ROYAL_GUARD + class_,
        JOB_ROYAL_GUARD_T | JOB_SORCERER_T | JOB_MINSTREL_T | JOB_WANDERER_T | JOB_SURA_T | JOB_GENETIC_T | JOB_SHADOW_CHASER_T => 81 - JOB_ROYAL_GUARD_T + class_,
        JOB_RUNE_KNIGHT2 | JOB_RUNE_KNIGHT_T2 => 75,
        JOB_ROYAL_GUARD2 | JOB_ROYAL_GUARD_T2 => 81,
        JOB_RANGER2 | JOB_RANGER_T2 => 77,
        JOB_MECHANIC2 | JOB_MECHANIC_T2 => 79,
        JOB_BABY_RUNE_KNIGHT | JOB_BABY_WARLOCK | JOB_BABY_RANGER | JOB_BABY_ARCH_BISHOP | JOB_BABY_MECHANIC | JOB_BABY_GUILLOTINE_CROSS | JOB_BABY_ROYAL_GUARD | JOB_BABY_SORCERER | JOB_BABY_MINSTREL | JOB_BABY_WANDERER | JOB_BABY_SURA | JOB_BABY_GENETIC | JOB_BABY_SHADOW_CHASER => 88 - JOB_BABY_RUNE_KNIGHT + class_,
        JOB_BABY_RUNE_KNIGHT2 => 88,
        JOB_BABY_ROYAL_GUARD2 => 94,
        JOB_BABY_RANGER2 => 90,
        JOB_BABY_MECHANIC2 => 92,
        JOB_SUPER_NOVICE_E | JOB_SUPER_BABY_E => 101 - JOB_SUPER_NOVICE_E + class_,
        JOB_KAGEROU | JOB_OBORO => 103 - JOB_KAGEROU + class_,
        JOB_REBELLION => 106,
        JOB_SUMMONER => 108,
        JOB_BABY_SUMMONER => 109,
        JOB_BABY_NINJA | JOB_BABY_KAGEROU | JOB_BABY_OBORO | JOB_BABY_TAEKWON | JOB_BABY_STAR_GLADIATOR | JOB_BABY_SOUL_LINKER | JOB_BABY_GUNSLINGER | JOB_BABY_REBELLION => 110 - JOB_BABY_NINJA + class_,
        JOB_BABY_STAR_GLADIATOR2 | JOB_STAR_EMPEROR | JOB_SOUL_REAPER | JOB_BABY_STAR_EMPEROR | JOB_BABY_SOUL_REAPER => 114 - JOB_BABY_STAR_GLADIATOR2 + class_,
        JOB_STAR_EMPEROR2 => 118,
        JOB_BABY_STAR_EMPEROR2 => 120,
        JOB_DRAGON_KNIGHT | JOB_MEISTER | JOB_SHADOW_CROSS | JOB_ARCH_MAGE | JOB_CARDINAL | JOB_WINDHAWK | JOB_IMPERIAL_GUARD | JOB_BIOLO | JOB_ABYSS_CHASER | JOB_ELEMENTAL_MASTER | JOB_INQUISITOR | JOB_TROUBADOUR | JOB_TROUVERE => 122 - JOB_DRAGON_KNIGHT + class_,
        JOB_WINDHAWK2 => 127,
        JOB_MEISTER2 => 123,
        JOB_DRAGON_KNIGHT2 => 122,
        JOB_IMPERIAL_GUARD2 => 128,
        JOB_SKY_EMPEROR | JOB_SOUL_ASCETIC | JOB_SHINKIRO | JOB_SHIRANUI | JOB_NIGHT_WATCH | JOB_HYPER_NOVICE | JOB_SPIRIT_HANDLER => 135 - JOB_SKY_EMPEROR + class_,
        JOB_SKY_EMPEROR2 => 135,
        _ => 199,
    };
    char_msg_job(n)
}

/// conf/msg_conf/char_msg.conf 0-141 and 199 (the class names the char server's job_name reads; the module's data
/// has map_msg.conf only).
fn char_msg_job(n: i32) -> &'static str {
    const JOBS: [&str; 142] = [
        "Novice", "Swordsman", "Magician", "Archer", "Acolyte", "Merchant", "Thief", "Knight", "Priest", "Wizard",
        "Blacksmith", "Hunter", "Assassin", "Crusader", "Monk", "Sage", "Rogue", "Alchemist", "Bard", "Dancer",
        "Wedding", "Super Novice", "Gunslinger", "Ninja", "Christmas", "High Novice", "High Swordsman",
        "High Magician", "High Archer", "High Acolyte", "High Merchant", "High Thief", "Lord Knight", "High Priest",
        "High Wizard", "Whitesmith", "Sniper", "Assassin Cross", "Paladin", "Champion", "Professor", "Stalker",
        "Creator", "Clown", "Gypsy", "Baby Novice", "Baby Swordsman", "Baby Magician", "Baby Archer", "Baby Acolyte",
        "Baby Merchant", "Baby Thief", "Baby Knight", "Baby Priest", "Baby Wizard", "Baby Blacksmith", "Baby Hunter",
        "Baby Assassin", "Baby Crusader", "Baby Monk", "Baby Sage", "Baby Rogue", "Baby Alchemist", "Baby Bard",
        "Baby Dancer", "Super Baby", "Taekwon", "Star Gladiator", "Soul Linker", "", "", "Summer", "Gangsi",
        "Death Knight", "Dark Collector", "Rune Knight", "Warlock", "Ranger", "Arch Bishop", "Mechanic",
        "Guillotine Cross", "Royal Guard", "Sorcerer", "Minstrel", "Wanderer", "Sura", "Genetic", "Shadow Chaser",
        "Baby Rune Knight", "Baby Warlock", "Baby Ranger", "Baby Arch Bishop", "Baby Mechanic",
        "Baby Guillotine Cross", "Baby Royal Guard", "Baby Sorcerer", "Baby Minstrel", "Baby Wanderer", "Baby Sura",
        "Baby Genetic", "Baby Shadow Chaser", "Expanded Super Novice", "Expanded Super Baby", "Kagerou", "Oboro",
        "Hanbok", "Rebellion", "Oktoberfest", "Summoner", "Baby Summoner", "Baby Ninja", "Baby Kagerou",
        "Baby Oboro", "Baby Taekwon", "Baby Star Gladiator", "Baby Soul Linker", "Baby Gunslinger",
        "Baby Rebellion", "Star Emperor", "Soul Reaper", "Baby Star Emperor", "Baby Soul Reaper", "Dragon Knight",
        "Meister", "Shadow Cross", "Arch Mage", "Cardinal", "Windhawk", "Imperial Guard", "Biolo", "Abyss Chaser",
        "Elemental Master", "Inquisitor", "Troubadour", "Trouvere", "Sky Emperor", "Soul Ascetic", "Shinkiro",
        "Shiranui", "Night Watch", "Hyper Novice", "Spirit Handler",
    ];
    if n == 199 {
        return "Unknown Job";
    }
    JOBS.get(n as usize).copied().unwrap_or("")
}

/// conf/msg_conf/char_msg.conf, the @accountinfo block (212-226).
const CHAR_MSG_212: &str = "No matches were found for your criteria, '%s'";
const CHAR_MSG_213: &str = "An error occurred, bother your admin about it.";
const CHAR_MSG_214: &str = "Your query returned the following %d results, please be more specific...";
const CHAR_MSG_215: &str = "[AID: %d] %s | %s | Level: %d/%d | %s";
const CHAR_MSG_216: &str = "No account with ID '%d' was found.";
const CHAR_MSG_217: &str = "-- Account %d --";
const CHAR_MSG_218: &str = "User: %s | GM Group: %d | State: %d";
const CHAR_MSG_221: &str = "Account e-mail: %s | Birthdate: %s";
const CHAR_MSG_222: &str = "Last IP: %s (%s)";
const CHAR_MSG_223: &str = "This user has logged in %d times, the last time was at %s";
const CHAR_MSG_224: &str = "-- Character Details --";
const CHAR_MSG_225: &str = "[Slot/CID: %d/%d] %s | %s | Level: %d/%d | %s";
const CHAR_MSG_226: &str = "This account doesn't have characters.";

/// The player's session (sd->fd): a character in the world whose connection is still there or has an eof mark
/// clif_parse has not handled (the fd is valid until then); None = fd 0 (no client, e.g. waiting out clif_delayquit).
fn fd_of(c: &Ctx, aid: u32) -> Option<Identity> {
    crate::pc::get(c, aid).filter(|p| !p.disconnected || crate::session::eof_pending(c, aid)).map(|p| p.identity)
}

/// clif_displaymessage(sd->fd, text) to a player by account id (nothing when the character is not in the world;
/// clif::displaymessage writes nothing to a closed or eof session).
fn say_to(c: &Ctx, aid: u32, text: &str) {
    if let Some(u) = crate::unit::get(c, aid) {
        crate::clif::displaymessage(c, &u, text);
    }
}

// ---------------------------------------------------------------------------------------------- the commands

/// sscanf(message, "%23[^\n]", atcmd_player_name) < 1 (with the `!message || !*message` test): the player name.
fn scan_name(message: &str) -> Option<String> {
    if message.is_empty() {
        return None;
    }
    let b = latin1(message);
    Scan::new(&b).set(23, b"\n")
}

/// sscanf(message, "%255s %23[^\n]", atcmd_output, atcmd_player_name) < 2: the time and the player name.
fn scan_time_name(message: &str) -> Option<(String, String)> {
    if message.is_empty() {
        return None;
    }
    let b = latin1(message);
    let mut s = Scan::new(&b);
    let t = s.word(255)?;
    s.ws();
    let n = s.set(23, b"\n")?;
    Some((t, n))
}

/// ACMD_FUNC(char_block): @block <char name> — the account of that character is blocked for good (login state 5)
/// and kicked if online; the login server's answer comes back through chrif_ack_login_req.
fn char_block(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some(name) = scan_name(message) else {
        out.say(&csprintf(msg_txt(1021), &[A::S(&latin1(command))])); // Please enter a player name (usage: %s <char name>).
        return -1;
    };
    chrif_req_login_operation(c, sd as i32, &latin1(&name), CHRIF_OP_LOGIN_BLOCK, 0, 0, 0);
    out.say(&csprintf(msg_txt(88), &[A::S(b"login")])); // Sending request to %s server...
    0
}

/// ACMD_FUNC(ban): @ban / @banish <time> <char name> — `time` as solve_time reads it (a positive change only).
fn ban(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some((modif, name)) = scan_time_name(message) else {
        out.say(&csprintf(msg_txt(1022), &[A::S(&latin1(command))])); // Please enter ban time and a player name (usage: %s <time> <char name>).
        return -1;
    };
    let timediff = solve_time(c, &latin1(&modif)); //discard seconds
    if timediff == 0 {
        //allow negative ?
        out.say(&csprintf(msg_txt(85), &[A::S(&latin1(command)), A::D(timediff as i64)])); // Invalid time for %s command (time=%d)
        out.say(msg_txt(702)); // Time parameter format is +/-<value> to alter. y/a = Year, m = Month, d/j = Day, h = Hour, n/mn = Minute, s = Second.
        return -1;
    }
    if timediff < 0 {
        out.say(msg_txt(1023)); // You are not allowed to alter the time of a ban.
        return -1;
    }
    chrif_req_login_operation(c, sd as i32, &latin1(&name), CHRIF_OP_LOGIN_BAN, timediff, 0, 0);
    out.say(&csprintf(msg_txt(88), &[A::S(b"login")])); // Sending request to %s server...
    0
}

/// ACMD_FUNC(char_ban): @charban / @char_ban <time> <char name> — the character alone is banned (char.unban_time).
fn char_ban(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some((modif, name)) = scan_time_name(message) else {
        out.say(&csprintf(msg_txt(1022), &[A::S(&latin1(command))])); // Please enter ban time and a player name (usage: %s <time> <char name>).
        return -1;
    };
    let timediff = solve_time(c, &latin1(&modif)); //discard seconds
    if timediff == 0 {
        //allow negative ?
        out.say(&csprintf(msg_txt(85), &[A::S(&latin1(command)), A::D(timediff as i64)])); // Invalid time for %s command (time=%d)
        out.say(msg_txt(702)); // Time parameter format is +/-<value> to alter. …
        return -1;
    }
    if timediff < 0 {
        out.say(msg_txt(1023)); // You are not allowed to alter the time of a ban.
        return -1;
    }
    chrif_req_charban(c, sd, &latin1(&name), timediff);
    out.say(&csprintf(msg_txt(88), &[A::S(b"char")])); // Sending request to %s server...
    0
}

/// ACMD_FUNC(char_unblock): @unblock <char name>.
fn char_unblock(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some(name) = scan_name(message) else {
        out.say(&csprintf(msg_txt(1021), &[A::S(&latin1(command))])); // Please enter a player name (usage: %s <char name>).
        return -1;
    };
    // send answer to login server via char-server
    chrif_req_login_operation(c, sd as i32, &latin1(&name), CHRIF_OP_LOGIN_UNBLOCK, 0, 0, 0);
    out.say(&csprintf(msg_txt(88), &[A::S(b"login")])); // Sending request to %s server...
    0
}

/// ACMD_FUNC(unban): @unban / @unbanish <char name>.
fn unban(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some(name) = scan_name(message) else {
        out.say(&csprintf(msg_txt(435), &[A::S(&latin1(command))])); // Please enter a player name (usage: %s <char name>).
        return -1;
    };
    chrif_req_login_operation(c, sd as i32, &latin1(&name), CHRIF_OP_LOGIN_UNBAN, 0, 0, 0);
    out.say(&csprintf(msg_txt(88), &[A::S(b"login")])); // Sending request to %s server...
    0
}

/// ACMD_FUNC(char_unban): @charunban / @char_unban <char name>.
fn char_unban(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let Some(name) = scan_name(message) else {
        out.say(&csprintf(msg_txt(435), &[A::S(&latin1(command))])); // Please enter a player name (usage: %s <char name>).
        return -1;
    };
    chrif_req_charunban(c, sd, &latin1(&name));
    out.say(&csprintf(msg_txt(88), &[A::S(b"char")])); // Sending request to %s server...
    0
}

/// ACMD_FUNC(email): @email <actual@email> <new@email> — the invoker's account (sd: for #email, the target's). The
/// login server changes it only when the actual address matches (and says nothing back either way).
fn email(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let b = latin1(message);
    let mut s = Scan::new(&b);
    let actual = s.word(99);
    let new = actual.as_ref().and_then(|_| s.word(99));
    let (Some(actual), Some(new)) = (actual, new) else {
        out.say(msg_txt(1151)); // Please enter 2 emails (usage: @email <actual@email> <new@email>).
        return -1;
    };
    let (actual, new) = (latin1(&actual), latin1(&new));
    if !e_mail_check(&actual) {
        out.say(msg_txt(144)); // Invalid actual email. If you have default e-mail, give a@a.com.
        return -1;
    } else if !e_mail_check(&new) {
        out.say(msg_txt(145)); // Invalid new email. Please enter a real e-mail.
        return -1;
    } else if strcmpi_eq(&new, b"a@a.com") {
        out.say(msg_txt(146)); // New email must be a real e-mail.
        return -1;
    } else if strcmpi_eq(&actual, &new) {
        out.say(msg_txt(147)); // New email must be different of the actual e-mail.
        return -1;
    }
    chrif_changeemail(c, sd, &actual, &new);
    out.say(msg_txt(148)); // Information sent to login-server via char-server.
    0
}

/// ACMD_FUNC(vip): VIP_ENABLE is not defined (config/core.hpp:52), so only "disabled" (the dispatcher then adds
/// "%s failed.").
fn vip(out: &Out) -> i32 {
    out.say(msg_txt(774)); // This command is disabled via configuration.
    -1
}

/// ACMD_FUNC(changesex) (`change_account`) / ACMD_FUNC(changecharsex): the Bard / Dancer skills are reset
/// (pc_resetskill flag 4), every equipment comes off (pc_unequipitem flag 3), then chrif_changesex asks the char
/// server and throws the player (sd: for #changesex, the target) out. No reply to the invoker.
fn changesex(c: &Ctx, sd: u32, change_account: bool) -> i32 {
    // nullpo_retr(-1, sd): the module has no dummy map_session_data for a script without a player
    if crate::pc::get(c, sd).is_none() {
        return -1;
    }
    crate::skilltree::pc_resetskill(c, sd, 4);
    // to avoid any problem with equipment and invalid sex, equipment is unequiped.
    // (sd->equip_index is read again for every slot: unequipping one item clears every slot it filled)
    for i in 0..crate::inventory::EQI_MAX {
        let idx = crate::inventory::st(c, sd).and_then(|s| s.equip_index.get(i).copied()).unwrap_or(-1);
        if idx >= 0 {
            crate::pc_equip::pc_unequipitem(c, sd, idx as i32, 3);
        }
    }
    chrif_changesex(c, sd, change_account);
    0
}

/// ACMD_FUNC(accinfo): @accinfo / @accountinfo <account id | char name> — the char server looks the account up
/// (a name is a SQL LIKE pattern) and the login server's record comes back as messages (to sd's fd: for #accinfo,
/// the target's).
fn accinfo(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let b = latin1(message);
    let query = if message.is_empty() || b.len() > NAME_LENGTH { None } else { Scan::new(&b).word(23) };
    let Some(query) = query else {
        out.say(msg_txt(1365)); // Usage: @accinfo/@accountinfo <account_id/char name>
        out.say(msg_txt(1366)); // You may search partial name by making use of '%' in the search, ex. "@accinfo %Mario%" lists all characters whose name contains "Mario".
        return -1;
    };
    intif_request_accinfo(c, fd_of(c, sd), sd, crate::pc::group_level(c, sd), &latin1(&query));
    0
}

// ---------------------------------------------------------------------------------------------- map server: chrif

/// map/chrif.cpp chrif_req_login_operation (S 0x2b0e): an account operation for the login server, via the char
/// server, on the character named `name` (safestrncpy NAME_LENGTH); `aid` is who asks (-1: the server itself, no
/// answer). `timediff` travels for BAN and VIP only.
pub(crate) fn chrif_req_login_operation(c: &Ctx, aid: i32, name: &[u8], op: u16, timediff: i32, val1: i32, val2: i32) -> i32 {
    // chrif_check(-1): the char server is always there
    let _ = val2; // WFIFOL(char_fd,40): no operation of this port reads it
    let timediff = if op == CHRIF_OP_LOGIN_BAN || op == CHRIF_OP_LOGIN_VIP { timediff } else { 0 };
    send(c, AccInter::LoginOperation { aid, name: name24(name), op, timediff, val1, sex: 0 });
    0
}

/// map/chrif.cpp chrif_changesex (S 0x2b0e with CHRIF_OP_LOGIN_CHANGESEX / CHRIF_OP_CHANGECHARSEX and, for the
/// character, the new sex): "Need disconnection to perform change-sex request..." and the client is thrown out
/// (clif_authfail_fd 15), or the character leaves at once when it has no client (map_quit). The change itself
/// happens while the player is away (chlogif_parse_ackchangesex / chlogif_parse_ackchangecharsex).
pub(crate) fn chrif_changesex(c: &Ctx, aid: u32, change_account: bool) -> i32 {
    // chrif_check(-1): the char server is always there
    let Some(ch) = crate::pc::character_of(c, aid) else { return 0 };
    let op = if change_account { CHRIF_OP_LOGIN_CHANGESEX } else { CHRIF_OP_CHANGECHARSEX };
    // WFIFOB(char_fd,32): written for the character's change only (not read otherwise)
    let sex = if change_account {
        0
    } else if ch.sex == SEX_MALE {
        SEX_FEMALE
    } else {
        SEX_MALE
    };
    send(c, AccInter::LoginOperation { aid: aid as i32, name: name24(&latin1(&ch.name)), op, timediff: 0, val1: 0, sex });
    say_to(c, aid, msg_txt(408)); //"Need disconnection to perform change-sex request..."
    if fd_of(c, aid).is_some() {
        crate::session::clif_authfail_fd(c, aid, 15);
    } else {
        crate::pc::quit(c, aid);
    }
    0
}

/// map/chrif.cpp chrif_changeemail (S 0x2b0c): memcpy of 40 bytes of each address (they passed e_mail_check: at
/// most 39 characters and the NUL).
fn chrif_changeemail(c: &Ctx, id: u32, actual: &[u8], new: &[u8]) -> i32 {
    // if (battle_config.etc_log) ShowInfo(...): etc_log is off (conf/battle/misc.conf)
    let cut = |b: &[u8]| b[..b.len().min(40)].to_vec();
    send(c, AccInter::ChangeEmail { account_id: id, actual: cut(actual), new: cut(new) });
    0
}

/// map/chrif.cpp chrif_req_charban (S 0x2b28).
fn chrif_req_charban(c: &Ctx, aid: u32, name: &[u8], timediff: i32) -> i32 {
    send(c, AccInter::CharBan { aid, timediff, name: name24(name) });
    0
}

/// map/chrif.cpp chrif_req_charunban (S 0x2b2a).
fn chrif_req_charunban(c: &Ctx, aid: u32, name: &[u8]) -> i32 {
    send(c, AccInter::CharUnban { aid, name: name24(name) });
    0
}

/// map/intif.cpp intif_request_accinfo (S 0x3007): `u_fd` is the requesting player's session, the answers come back
/// as messages to it.
fn intif_request_accinfo(c: &Ctx, u_fd: Option<Identity>, aid: u32, group_lv: i32, query: &[u8]) -> i32 {
    // CheckForCharServer(): the char server is always there
    send(c, AccInter::AccInfoReq { u_fd, u_aid: aid, group_lv, query: name24(query) });
    1
}

/// map/chrif.cpp chrif_ack_login_req (R 0x2b0f): the char server's answer to chrif_req_login_operation, shown to
/// the player who asked (0: login-server request done / 4, 1: player not found, 2: gm level too low, 3: login-server
/// offline). It comes before the login server has done anything.
fn chrif_ack_login_req(c: &Ctx, aid: i32, player_name: &[u8], mut type_: u16, answer: u16) {
    if aid < 0 || crate::pc::get(c, aid as u32).is_none() {
        log::error!("chrif_ack_login_req failed - player not online.");
        return;
    }
    let action: String = match type_ {
        CHRIF_OP_LOGIN_CHANGESEX | CHRIF_OP_CHANGECHARSEX | CHRIF_OP_LOGIN_BLOCK | CHRIF_OP_LOGIN_BAN | CHRIF_OP_LOGIN_UNBLOCK | CHRIF_OP_LOGIN_UNBAN => {
            if type_ == CHRIF_OP_CHANGECHARSEX {
                type_ = CHRIF_OP_LOGIN_CHANGESEX; // So we don't have to create a new msgstring.
            }
            msg_txt(427 + type_).chars().take(24).collect() //block|ban|unblock|unban|change the sex of
        }
        CHRIF_OP_LOGIN_VIP => {
            // battle_config.disp_servervip_msg: no (conf/battle/misc.conf)
            return;
        }
        _ => "???".into(),
    };
    let (a, n) = (latin1(&action), player_name);
    let nl = A::D(NAME_LENGTH as i64);
    let output = match answer {
        0 | 4 => csprintf(msg_txt(424), &[A::S(&a), nl, A::S(n)]), //Login-serv has been asked to %s '%.*s'.
        1 => csprintf(msg_txt(425), &[nl, A::S(n)]),
        2 => csprintf(msg_txt(426), &[A::S(&a), nl, A::S(n)]),
        3 => csprintf(msg_txt(427), &[A::S(&a), nl, A::S(n)]),
        _ => String::new(),
    };
    say_to(c, aid as u32, &output);
}

/// map/chrif.cpp chrif_changedsex (R 0x2b0d): the sex of account `acc` (or one of its characters) has changed. The
/// player was thrown out by chrif_changesex and is normally gone by now; one still in the world (without a client:
/// prevent_logout kept it for clif_delayquit) has the gender-split skills reset, the other-gender version of its
/// class, and leaves at once. The module's `character` row is the char server's and the map server's copy at once:
/// sd->status.sex, the map server's own copy, is the unit's view data sex here, and the class was already swapped
/// on the row by chlogif_parse_change_sex_sub (the same pairs as the map server's ±1).
fn chrif_changedsex(c: &Ctx, acc: u32, sex: u8) -> i32 {
    // if ( battle_config.etc_log ) ShowNotice(...): etc_log is off
    if crate::pc::get(c, acc).is_none() {
        return 0;
    }
    let Some(mut u) = crate::unit::get(c, acc) else { return 0 };
    //Normally there should not be a char logged on right now!
    if u.vd.sex == sex {
        return 0; //Do nothing? Likely safe.
    }
    u.vd.sex = sex;
    crate::unit::save(c, &u);
    // Reset skills of gender split jobs.
    let class_ = crate::status::data::sd_get(c, acc).map(|s| s.class_).unwrap_or(0);
    use crate::db::jobs::ids::{MAPID_BARDDANCER, MAPID_KAGEROUOBORO, MAPID_SECONDMASK};
    if class_ & MAPID_SECONDMASK == MAPID_BARDDANCER || class_ & MAPID_SECONDMASK == MAPID_KAGEROUOBORO {
        use crate::db::skills as sk;
        const RANGES: [(u16, u16); 6] = [
            // Bard class exclusive skills
            (sk::BA_MUSICALLESSON, sk::BA_APPLEIDUN),
            // Dancer class exclusive skills
            (sk::DC_DANCINGLESSON, sk::DC_SERVICEFORYOU),
            // Minstrel class exclusive skills
            (sk::MI_RUSH_WINDMILL, sk::MI_HARMONIZE),
            // Wanderer class exclusive skills
            (sk::WA_SWING_DANCE, sk::WA_MOONLIT_SERENADE),
            // Kagerou class exclusive skills
            (sk::KG_KAGEHUMI, sk::KG_KAGEMUSYA),
            // Oboro class exclusive skills
            (sk::OB_ZANGETSU, sk::OB_AKAITSUKI),
        ];
        if let Some(mut s) = crate::skilltree::Skills::load(c, acc) {
            let mut points: u32 = 0;
            for (start, end) in RANGES {
                for skill_id in start..=end {
                    let sk_idx = sk::skill_db().get_index(skill_id, false);
                    let slot = s.at(sk_idx);
                    if slot.id > 0 && slot.flag == crate::status::defs::SKILL_FLAG_PERMANENT {
                        points = points.wrapping_add(slot.lv as u32);
                        s.put(sk_idx, crate::skilltree::Slot { id: 0, lv: 0, flag: slot.flag });
                    }
                }
            }
            s.save(c);
            if let Some(mut ch) = crate::pc::character_of(c, acc) {
                ch.skill_point = ch.skill_point.wrapping_add(points);
                crate::status::put_char(c, &ch);
            }
        }
        crate::status::clif::updatestatus(c, acc, crate::status::consts::SP_SKILLPOINT);
        // Change to other gender version of the job if needed: sd->status.class_ ∓ 1 and body = class_ — already on
        // the shared row (chlogif_parse_change_sex_sub). sd->class_ Does not need to be updated as both versions of
        // the job are the same.
    }
    // save character: sd->login_id1++ (the module has no login ids)
    say_to(c, acc, msg_txt(409)); //"Your sex has been changed (need disconnection by the server)..."
    crate::session::set_eof(c, acc); // forced to disconnect for the change
    crate::pc::quit(c, acc); // Remove leftovers (e.g. autotrading) [Paradox924X]
    0
}

/// map/chrif.cpp chrif_ban (R 0x2b14): the account (res 0: a change of state, 1: a ban) or the character (res 2: a
/// charban, `id` is a char id) of a player in the world was blocked / banned: a message, then the client is thrown
/// out (set_eof) and the character leaves at once (map_quit).
fn chrif_ban(c: &Ctx, id: u32, res: u8, value: u32) -> i32 {
    // if ( battle_config.etc_log ) ShowNotice(...): etc_log is off
    let aid = if res == 2 { crate::pc::charid2sd(c, id).map(|u| u.id) } else { crate::pc::get(c, id).map(|p| p.account_id) };
    let Some(aid) = aid.filter(|_| (id as i32) >= 0) else {
        //nothing to do on map if player not connected
        return 0;
    };
    // sd->login_id1++: the module has no login ids
    if res == 0 {
        let ret_status = value as i32; // status or final date of a banishment
        say_to(c, aid, msg_txt(state_msg(ret_status))); // 0<ret_status<=9: 411+ret_status; 100: 421; else 420 "Your account has not more authorised."
    } else if res == 1 || res == 2 {
        let timestamp = value as i64; // status or final date of a banishment
        let strtime = crate::script::cfmt::strftime(b"%d-%m-%Y %H:%M:%S", 24, &localtime(timestamp));
        let who: &[u8] = if res == 2 { b"char" } else { b"account" };
        say_to(c, aid, &csprintf(msg_txt(423), &[A::S(who), A::S(&strtime)])); //"Your %s has been banished until %s "
    }
    crate::session::set_eof(c, aid); // forced to disconnect for the change
    crate::pc::quit(c, aid); // Remove leftovers (e.g. autotrading) [Paradox924X]
    0
}

/// map/intif.cpp intif_parse_MessageToFD (R 0x3807): a message from the char server for the player on session
/// `u_fd`, shown when that session still has the same player.
fn intif_parse_message_to_fd(c: &Ctx, u_fd: Option<Identity>, aid: u32, msg: &[u8]) {
    let Some(fd) = u_fd else { return };
    //check if the player still online
    let Some(p) = crate::pc::by_identity(c, fd) else { return };
    /* matching e.g. previous fd owner didn't dc during request or is still the same */
    if p.account_id == aid {
        let end = msg.iter().position(|&b| b == 0).unwrap_or(msg.len()).min(511);
        say_to(c, aid, &from_latin1(&msg[..end]));
    }
}

// ---------------------------------------------------------------------------------------------- char server

/// char/char.cpp char_disconnect_player: a client of this account on the char server (the character screen) is
/// disconnected. The module's character screen is the account binding (account.rs `session`) of a connected client
/// whose account has no character in the world; set_eof on that link is the end of the binding (the client is back
/// at the login screen, as rAthena's client after losing the char server).
fn char_disconnect_player(c: &Ctx, account_id: u32) {
    if crate::pc::get(c, account_id).is_some() {
        return; // on the map server: no char-server session
    }
    let ids: Vec<Identity> = c.db().session().account_id().filter(account_id).filter(|s| s.online).map(|s| s.identity).collect();
    for id in ids {
        c.db().session().identity().delete(id);
    }
}

/// char/char_mapif.cpp chmapif_parse_fwlog_changestatus (0x2b0e): the character named `name` is looked up; its
/// account operation goes to the login server (or, for the character's sex, is done here), and the one who asked
/// (`aid`, not -1) gets 0x2b0f with the result (0 done, 1 player not found; 2 / 3 are never produced here) — except
/// for the sex changes. The answer does not wait for the login server.
fn chmapif_parse_fwlog_changestatus(c: &Ctx, aid: i32, name: &[u8], operation: u16, timediff: i32, val1: i32, sex: u8) {
    let mut result: u16 = 0; // 0-login-server request done, 1-player not found, 2-gm level too low, 3-login-server offline, 4-current group level > VIP group level
    let mut answer = true;
    let (timediff, val1, sex) = if operation == CHRIF_OP_LOGIN_BAN || operation == CHRIF_OP_LOGIN_VIP {
        (timediff, val1, SEX_MALE)
    } else if operation == CHRIF_OP_CHANGECHARSEX {
        (0, 0, sex)
    } else {
        (0, 0, SEX_MALE)
    };
    match char_by_name(c, name) {
        None => result = 1, // 1-player not found
        Some(ch) => {
            let (t_aid, t_cid) = (ch.account_id, ch.id);
            // chlogif_isconnected(): the login server is always there (3-login-server offline never happens)
            //FIXME: need to move this check to login server [ultramage] (2-gm level too low is never checked)
            match operation {
                CHRIF_OP_LOGIN_BLOCK => send(c, AccInter::LoginUpdAccState { account_id: t_aid, state: 5 }), // new account status
                CHRIF_OP_LOGIN_BAN => send(c, AccInter::LoginBanAcc { account_id: t_aid, timediff }),
                CHRIF_OP_LOGIN_UNBLOCK => send(c, AccInter::LoginUpdAccState { account_id: t_aid, state: 0 }), // new account status
                CHRIF_OP_LOGIN_UNBAN => send(c, AccInter::LoginUnbanAcc { account_id: t_aid }),
                CHRIF_OP_LOGIN_CHANGESEX => {
                    answer = false;
                    send(c, AccInter::LoginChgSex { account_id: t_aid });
                }
                CHRIF_OP_LOGIN_VIP => {
                    answer = val1 & 4 != 0; // vip_req val1=type, &1 login send return, &2 update timestamp, &4 map send answer
                    // chlogif_reqvipdata: VIP_ENABLE is off, nothing goes to the login server
                }
                CHRIF_OP_CHANGECHARSEX => {
                    answer = false;
                    chlogif_parse_ackchangecharsex(c, t_cid, sex);
                }
                _ => {}
            }
        }
    }
    // send answer if a player asks, not if the server asks
    if aid != -1 && answer {
        // Don't send answer for changesex/changecharsex
        send(c, AccInter::AckLoginReq { aid, name: name24(name), op: operation, answer: result });
    }
}

/// char/char_mapif.cpp chmapif_parse_reqnewemail (0x2b0c): forwarded to the login server as 0x2722.
fn chmapif_parse_reqnewemail(c: &Ctx, account_id: u32, actual: Vec<u8>, new: Vec<u8>) {
    // chlogif_isconnected(): the login server is always there
    send(c, AccInter::LoginChangeEmail { account_id, actual, new });
}

/// char/char_mapif.cpp chmapif_parse_reqcharban (0x2b28): the character's `unban_time` moves to max(now, unban_time)
/// + timediff (0 when that is in the past); while it is still to come, every map server is told (0x2b14, res 2) and
/// a char-server client of the account is dropped. No answer to the one who asked.
fn chmapif_parse_reqcharban(c: &Ctx, _aid: u32, timediff: i32, name: &[u8]) {
    let Some(mut ch) = char_by_name(c, name) else {
        return; // 1-player not found
    };
    let (t_aid, t_cid) = (ch.account_id, ch.id);
    let mut unban_time = ch.unban_time as i64;
    let now = now_secs(c);
    if timediff < 0 && unban_time == 0 {
        return; //attemp to reduce time of a non banned account ?!?
    } else if unban_time < now {
        unban_time = now; //new entry
    }
    unban_time += timediff as i64; //alterate the time
    if unban_time < now {
        unban_time = 0; //we have totally reduce the time
    }
    // UPDATE char SET unban_time (an int(11) unsigned column: MySQL keeps the value in its range)
    ch.unban_time = unban_time.clamp(0, u32::MAX as i64) as u32;
    c.db().character().id().update(ch);
    // condition applies; send to all map-servers to disconnect the player
    if unban_time > now {
        send(c, AccInter::Ban { id: t_cid, res: 2, value: unban_time as u32 });
        // disconnect player if online on char-server
        char_disconnect_player(c, t_aid);
    }
}

/// char/char_mapif.cpp chmapif_parse_reqcharunban (0x2b2a): `UPDATE char SET unban_time = 0 WHERE name = …`.
fn chmapif_parse_reqcharunban(c: &Ctx, _aid: u32, name: &[u8]) {
    if let Some(mut ch) = char_by_name(c, name) {
        ch.unban_time = 0;
        c.db().character().id().update(ch);
    }
}

/// char/char_logif.cpp chlogif_parse_accbannotification (0x2731): every map server is told (0x2b14) and a
/// char-server client of the account is dropped.
fn chlogif_parse_accbannotification(c: &Ctx, account_id: u32, kind: u8, value: u32) {
    // send to all map-servers to disconnect the player
    send(c, AccInter::Ban { id: account_id, res: kind, value });
    // disconnect player if online on char-server
    char_disconnect_player(c, account_id);
}

/// char/char_logif.cpp chlogif_parse_change_sex_sub: the character's class becomes the other gender's version (the
/// Bard / Dancer lines, Kagerou / Oboro …), its equipment is taken off in the inventory table, its looks cleared and
/// its sex set; a guild member's gender follows (inter_guild_sex_changed). Deliberately different: rAthena's map
/// server later saves its own copy of the inventory over these rows when the character was still there (the
/// @changecharsex order: the equip switch window keeps its items there); here the rows are the only copy.
fn chlogif_parse_change_sex_sub(c: &Ctx, sex: u8, acc: u32, char_id: u32, class_: i32, guild_id: u32) {
    use crate::db::jobs::ids::*;
    // job modification
    let class_ = match class_ {
        JOB_BARD => JOB_DANCER,
        JOB_DANCER => JOB_BARD,
        JOB_CLOWN => JOB_GYPSY,
        JOB_GYPSY => JOB_CLOWN,
        JOB_BABY_BARD => JOB_BABY_DANCER,
        JOB_BABY_DANCER => JOB_BABY_BARD,
        JOB_MINSTREL => JOB_WANDERER,
        JOB_WANDERER => JOB_MINSTREL,
        JOB_MINSTREL_T => JOB_WANDERER_T,
        JOB_WANDERER_T => JOB_MINSTREL_T,
        JOB_BABY_MINSTREL => JOB_BABY_WANDERER,
        JOB_BABY_WANDERER => JOB_BABY_MINSTREL,
        JOB_KAGEROU => JOB_OBORO,
        JOB_OBORO => JOB_KAGEROU,
        JOB_BABY_KAGEROU => JOB_BABY_OBORO,
        JOB_BABY_OBORO => JOB_BABY_KAGEROU,
        JOB_TROUBADOUR => JOB_TROUVERE,
        JOB_TROUVERE => JOB_TROUBADOUR,
        JOB_SHINKIRO => JOB_SHIRANUI,
        JOB_SHIRANUI => JOB_SHINKIRO,
        other => other,
    };
    // UPDATE inventory SET equip = 0, equip_switch = 0 WHERE char_id
    use crate::inventory::inventory;
    let rows: Vec<crate::inventory::InventoryItem> = c.db().inventory().char_id().filter(char_id).filter(|r| r.item.equip != 0 || r.item.equip_switch != 0).collect();
    for mut r in rows {
        r.item.equip = 0;
        r.item.equip_switch = 0;
        c.db().inventory().id().update(r);
    }
    // UPDATE char SET class, body, weapon … robe = 0, sex WHERE char_id (`class` is a smallint(6) unsigned column)
    if let Some(mut ch) = c.db().character().id().find(char_id) {
        ch.class = class_ as u16;
        ch.body = class_ as u16;
        ch.weapon = 0;
        ch.shield = 0;
        ch.head_top = 0;
        ch.head_mid = 0;
        ch.head_bottom = 0;
        ch.robe = 0;
        ch.sex = if sex == SEX_MALE { SEX_MALE } else { SEX_FEMALE };
        c.db().character().id().update(ch);
    }
    if guild_id != 0 {
        // If there is a guild, update the guild_member data [Skotlex]
        crate::int_guild::inter_guild_sex_changed(c, guild_id as i32, acc, char_id, sex as i16);
    }
}

/// char/char_logif.cpp chlogif_parse_ackchangesex (0x2723): the account's sex changed; every character of it gets
/// the change (chlogif_parse_change_sex_sub, in the table's order, at most MAX_CHARS), then every map server is told
/// (0x2b0d).
fn chlogif_parse_ackchangesex(c: &Ctx, acc: u32, sex: u8) {
    if acc > 0 {
        // TODO: Is this even possible?
        // the char server's auth node (node->sex = sex): the module's login binding has no sex of its own
        let mut chars: Vec<Character> = c.db().character().account_id().filter(acc).collect();
        chars.sort_by_key(|ch| ch.id);
        for ch in chars.into_iter().take(MAX_CHARS) {
            chlogif_parse_change_sex_sub(c, sex, acc, ch.id, ch.class as i16 as i32, ch.guild_id);
        }
    }
    // notify all mapservers about this change
    send(c, AccInter::ChangedSex { account_id: acc, sex });
}

/// char/char_logif.cpp chlogif_parse_ackchangecharsex: one character's sex changes (chlogif_parse_change_sex_sub), a
/// char-server client of its account is dropped, and every map server is told (0x2b0d).
fn chlogif_parse_ackchangecharsex(c: &Ctx, char_id: u32, sex: u8) -> i32 {
    // get character data
    let Some(ch) = c.db().character().id().find(char_id) else { return 1 };
    let (account_id, class_, guild_id) = (ch.account_id, ch.class as i32, ch.guild_id);
    chlogif_parse_change_sex_sub(c, sex, account_id, char_id, class_, guild_id);
    // disconnect player if online on char-server
    char_disconnect_player(c, account_id);
    // notify all mapservers about this change
    send(c, AccInter::ChangedSex { account_id, sex });
    0
}

/// char/inter.cpp inter_to_fd: a message (vsnprintf into 512 bytes) for the player on session `u_fd` (0x3807).
fn inter_to_fd(c: &Ctx, u_fd: Option<Identity>, aid: u32, msg: String) {
    let mut b = latin1(&msg);
    b.truncate(511);
    send(c, AccInter::MessageToFd { u_fd, aid, msg: b });
}

/// Whether the character is online for the char server (`char`.`online`: set when it is selected — "set char as
/// online prior to loading its data" — and while it is on the map server).
fn char_online(c: &Ctx, ch: &Character) -> bool {
    use crate::pc::pc;
    c.db().pc().char_id().find(ch.id).is_some() || c.db().session().account_id().filter(ch.account_id).any(|s| s.selected_char == Some(ch.id))
}

/// char/inter.cpp mapif_parse_accinfo (0x3007): a query below START_ACCOUNT_NUM is a character name (`LIKE`, at most
/// 10 rows in the table's order): no match → message 212, several → the list (214, 215 each), one → its account;
/// an account id goes to the login server as it is (atoi into an uint32: "-5" is account 4294967291).
fn mapif_parse_accinfo(c: &Ctx, u_fd: Option<Identity>, u_aid: u32, _u_group: i32, query: &[u8]) {
    let query = name24(query);
    let mut account_id = crate::cstr::atoi(&from_latin1(&query)) as u32;
    if account_id < START_ACCOUNT_NUM {
        // is string
        let mut rows: Vec<Character> = c.db().character().iter().filter(|ch| sql_like(&latin1(&ch.name), &query)).collect();
        rows.sort_by_key(|ch| ch.id);
        rows.truncate(10);
        if rows.is_empty() {
            inter_to_fd(c, u_fd, u_aid, csprintf(CHAR_MSG_212, &[A::S(&query)])); // No matches were found for your criteria, '%s'
            return;
        }
        if rows.len() == 1 {
            //we found a perfect match
            account_id = rows[0].account_id;
        } else {
            // more than one, listing... [Dekamaster/Nightroad]
            inter_to_fd(c, u_fd, u_aid, csprintf(CHAR_MSG_214, &[A::D(rows.len() as i64)])); // Your query returned the following %d results, please be more specific...
            for ch in &rows {
                let name = name24(&latin1(&ch.name));
                let online: &[u8] = if char_online(c, ch) { b"Online" } else { b"Offline" };
                let job = latin1(char_job_name(ch.class as i16 as i32));
                inter_to_fd(
                    c,
                    u_fd,
                    u_aid,
                    csprintf(CHAR_MSG_215, &[A::D(ch.account_id as i64), A::S(&name), A::S(&job), A::D(ch.base_level as i16 as i64), A::D(ch.job_level as i16 as i64), A::S(online)]),
                ); // [AID: %d] %s | %s | Level: %d/%d | %s
            }
            return;
        }
    }
    /* it will only get here if we have a single match then ask login-server to fetch the `login` record */
    if account_id == 0 {
        inter_to_fd(c, u_fd, u_aid, CHAR_MSG_213.to_string()); // An error occurred, bother your admin about it.
        return;
    }
    // chlogif_req_accinfo (0x2720; loginif_check: the login server is always there)
    send(c, AccInter::LoginAccInfo { u_fd, u_aid, account_id: account_id as i32 });
}

/// char/inter.cpp mapif_accinfo_ack (the 0x2721 answer, via chlogif_parse_AccInfoAck): the login record and the
/// account's characters (ORDER BY char_num, at most MAX_CHARS) as messages to the player who asked. "Last IP" is
/// geoip_getcountry(str2ip(last_ip)): "Unknown" (db/GeoIP.dat has country 0 for every reserved / private address,
/// and the module never has an address: last_ip is "").
fn mapif_accinfo_ack(c: &Ctx, u_fd: Option<Identity>, u_aid: u32, account_id: i32, info: Option<&AccInfo>) {
    // if (map_fd <= 0 || !session_isActive(map_fd)): the map server is always there
    let Some(i) = info else {
        inter_to_fd(c, u_fd, u_aid, csprintf(CHAR_MSG_216, &[A::D(account_id as i64)])); // No account with ID '%d' was found.
        return;
    };
    inter_to_fd(c, u_fd, u_aid, csprintf(CHAR_MSG_217, &[A::D(account_id as i64)])); // -- Account %d --
    inter_to_fd(c, u_fd, u_aid, csprintf(CHAR_MSG_218, &[A::S(&i.userid), A::D(i.group_id as i64), A::D(i.state as i64)])); // User: %s | GM Group: %d | State: %d
    inter_to_fd(c, u_fd, u_aid, csprintf(CHAR_MSG_221, &[A::S(&i.email), A::S(&i.birthdate)])); // Account e-mail: %s | Birthdate: %s
    inter_to_fd(c, u_fd, u_aid, csprintf(CHAR_MSG_222, &[A::S(&i.last_ip), A::S(b"Unknown")])); // Last IP: %s (%s)
    inter_to_fd(c, u_fd, u_aid, csprintf(CHAR_MSG_223, &[A::D(i.logincount as i64), A::S(&i.lastlogin)])); // This user has logged in %d times, the last time was at %s
    inter_to_fd(c, u_fd, u_aid, CHAR_MSG_224.to_string()); // -- Character Details --
    let mut chars: Vec<Character> = c.db().character().account_id().filter(account_id as u32).collect();
    chars.sort_by_key(|ch| (ch.slot, ch.id));
    chars.truncate(MAX_CHARS);
    if chars.is_empty() {
        inter_to_fd(c, u_fd, u_aid, CHAR_MSG_226.to_string()); // This account doesn't have characters.
        return;
    }
    for ch in &chars {
        let name = name24(&latin1(&ch.name));
        let online: &[u8] = if char_online(c, ch) { b"Online" } else { b"Offline" };
        let job = latin1(char_job_name(ch.class as i32));
        inter_to_fd(
            c,
            u_fd,
            u_aid,
            csprintf(
                CHAR_MSG_225,
                &[A::D(ch.slot as i64), A::D(ch.id as i64), A::S(&name), A::S(&job), A::D(ch.base_level as i16 as i64), A::D(ch.job_level as i16 as i64), A::S(online)],
            ),
        ); // [Slot/CID: %d/%d] %s | %s | Level: %d/%d | %s
    }
}

// ---------------------------------------------------------------------------------------------- login server

/// login/loginchrif.cpp logchrif_parse_reqchangemail (0x2722): the address changes when the actual one is the
/// account's (strcmpi); every refusal is only a notice in the login server's log.
fn logchrif_parse_reqchangemail(c: &Ctx, account_id: u32, actual: &[u8], new: &[u8]) {
    let actual = &actual[..actual.iter().position(|&b| b == 0).unwrap_or(actual.len()).min(39)];
    let new = &new[..new.iter().position(|&b| b == 0).unwrap_or(new.len()).min(39)];
    let exists = c.db().account().id().find(account_id).is_some();
    let mut acc = login_ext_get(c.db(), account_id);
    if !e_mail_check(actual) {
        log::info!("Char-server: Attempt to modify an e-mail on an account (@email GM command), but actual email is invalid (account: {account_id})");
    } else if !e_mail_check(new) {
        log::info!("Char-server: Attempt to modify an e-mail on an account (@email GM command) with a invalid new e-mail (account: {account_id})");
    } else if strcmpi_eq(new, b"a@a.com") {
        log::info!("Char-server: Attempt to modify an e-mail on an account (@email GM command) with a default e-mail (account: {account_id})");
    } else if !exists {
        log::info!("Char-server: Attempt to modify an e-mail on an account (@email GM command), but account doesn't exist (account: {account_id}).");
    } else if !strcmpi_eq(&latin1(&acc.email), actual) {
        log::info!("Char-server: Attempt to modify an e-mail on an account (@email GM command), but actual e-mail is incorrect (account: {account_id}, actual e-mail: {}, proposed e-mail: {}).", acc.email, from_latin1(actual));
    } else {
        acc.email = from_latin1(new);
        log::info!("Char-server: Modify an e-mail on an account (@email GM command) (account: {account_id}, new e-mail: {}).", acc.email);
        // Save
        login_ext_put(c.db(), acc);
    }
}

/// login/loginchrif.cpp logchrif_parse_requpdaccstate (0x2724): the account's state changes; a state other than 0
/// is announced to the char servers (0x2731, kind 0).
fn logchrif_parse_requpdaccstate(c: &Ctx, account_id: u32, state: u32) {
    if c.db().account().id().find(account_id).is_none() {
        log::info!("Char-server: Error of Status change (account: {account_id} not found, suggested status {state}).");
        return;
    }
    let mut acc = login_ext_get(c.db(), account_id);
    if acc.state == state {
        log::info!("Char-server:  Error of Status change - actual status is already the good status (account: {account_id}, status {state}).");
        return;
    }
    log::info!("Char-server: Status change (account: {account_id}, new status {state}).");
    acc.state = state;
    // Save
    login_ext_put(c.db(), acc);
    // notify other servers
    if state != 0 {
        send(c, AccInter::AccBanNotification { account_id, kind: 0, value: state }); // 0: change of state, 1: ban
    }
}

/// login/loginchrif.cpp logchrif_parse_reqbanacc (0x2725): the account's unban_time moves to (now, or the running
/// ban's end) + timediff and is announced to the char servers (0x2731, kind 1); a result not in the future is refused
/// (a notice).
fn logchrif_parse_reqbanacc(c: &Ctx, account_id: u32, timediff: i32) {
    if c.db().account().id().find(account_id).is_none() {
        log::info!("Char-server: Error of ban request (account: {account_id} not found).");
        return;
    }
    let mut acc = login_ext_get(c.db(), account_id);
    let now = now_secs(c);
    let mut timestamp = if acc.unban_time == 0 || (acc.unban_time as i64) < now {
        now // new ban
    } else {
        acc.unban_time as i64 // add to existing ban
    };
    timestamp += timediff as i64;
    if timestamp == -1 {
        log::info!("Char-server: Error of ban request (account: {account_id}, invalid date).");
    } else if timestamp <= now || timestamp == 0 {
        log::info!("Char-server: Error of ban request (account: {account_id}, new date unbans the account).");
    } else {
        log::info!("Char-server: Ban request (account: {account_id}, new final date of banishment: {timestamp}).");
        // the `unban_time` int(11) unsigned column keeps the value in its range
        acc.unban_time = timestamp.clamp(0, u32::MAX as i64) as u32;
        // Save
        login_ext_put(c.db(), acc);
        send(c, AccInter::AccBanNotification { account_id, kind: 1, value: timestamp as u32 }); // 0: change of status, 1: ban
    }
}

/// login/loginchrif.cpp logchrif_parse_reqchgsex (0x2727): the account's sex is reversed (a server account 'S' is
/// refused: the module has none) and announced to the char servers (0x2723).
fn logchrif_parse_reqchgsex(c: &Ctx, account_id: u32) {
    let Some(mut acc) = c.db().account().id().find(account_id) else {
        log::info!("Char-server: Error of sex change (account: {account_id} not found).");
        return;
    };
    let sex = if acc.sex == SEX_MALE { SEX_FEMALE } else { SEX_MALE }; //Change gender
    log::info!("Char-server: Sex change (account: {account_id}, new sex {}).", if sex == SEX_MALE { 'M' } else { 'F' });
    acc.sex = sex;
    // Save
    c.db().account().id().update(acc);
    // announce to other servers
    send(c, AccInter::AckChangeSex { account_id, sex });
}

/// login/loginchrif.cpp logchrif_parse_requnbanacc (0x272a): a running or finished ban is lifted (unban_time 0).
fn logchrif_parse_requnbanacc(c: &Ctx, account_id: u32) {
    if c.db().account().id().find(account_id).is_none() {
        log::info!("Char-server: Error of UnBan request (account: {account_id} not found).");
        return;
    }
    let mut acc = login_ext_get(c.db(), account_id);
    if acc.unban_time == 0 {
        log::info!("Char-server: Error of UnBan request (account: {account_id}, no change for unban date).");
        return;
    }
    log::info!("Char-server: UnBan request (account: {account_id}).");
    acc.unban_time = 0;
    login_ext_put(c.db(), acc);
}

/// login/loginchrif.cpp logchrif_parse_accinfo (0x2720): the account's record goes back to the char server (0x2721).
/// The `login` columns without a source here: last_ip "" (the module never sees an address), birthdate "" (NULL:
/// nothing sets it); lastlogin is `Account::last_login` as the DATETIME column reads ("%Y-%m-%d %H:%M:%S", server
/// time), "" before the first login.
fn logchrif_parse_accinfo(c: &Ctx, u_fd: Option<Identity>, u_aid: u32, account_id: i32) {
    let Some(acc) = c.db().account().id().find(account_id as u32) else {
        send(c, AccInter::AccInfoAck { u_fd, u_aid, account_id, info: None });
        return;
    };
    let ext = login_ext_get(c.db(), acc.id);
    let lastlogin = match acc.last_login {
        Some(t) => crate::script::cfmt::strftime(b"%Y-%m-%d %H:%M:%S", 24, &localtime(secs_of(t))),
        None => Vec::new(),
    };
    let email = latin1(&ext.email);
    let info = AccInfo {
        group_id: acc.group_id as i32,
        logincount: ext.logincount as i32,
        state: ext.state as i32,
        email: email[..email.len().min(39)].to_vec(),
        last_ip: Vec::new(),
        lastlogin,
        birthdate: Vec::new(),
        userid: name24(&latin1(&acc.username)),
    };
    send(c, AccInter::AccInfoAck { u_fd, u_aid, account_id, info: Some(Box::new(info)) });
}

#[cfg(test)]
mod tests {
    /// login.cpp login_mmo_auth:358: an expiration time of 0 never expires; one in the past refuses (2), the second
    /// it names does not yet (`<`).
    #[test]
    fn expiration_time_is_login_mmo_auths() {
        assert!(!super::expired(0, 1_800_000_000));
        assert!(super::expired(1, 1_800_000_000));
        assert!(!super::expired(1_800_000_000, 1_800_000_000));
        assert!(super::expired(1_800_000_000, 1_800_000_001));
        assert!(!super::expired(1_800_000_100, 1_800_000_000));
    }

    use super::*;

    /// 2026-09-27 21:30:00 at UTC-3 (the oracle's time zone).
    const NOW: i64 = 1_790_555_400;

    #[test]
    fn local_clock() {
        let tm = localtime(NOW);
        assert_eq!((tm.year + 1900, tm.mon + 1, tm.mday, tm.hour, tm.min, tm.sec), (2026, 9, 27, 21, 30, 0));
        assert_eq!(mktime(&tm), NOW);
    }

    #[test]
    fn solve_time_units() {
        assert_eq!(solve_time_at(NOW, b"1h"), 3600.0);
        assert_eq!(solve_time_at(NOW, b"+1h"), 3600.0);
        assert_eq!(solve_time_at(NOW, b"-1h"), -3600.0);
        assert_eq!(solve_time_at(NOW, b"1d"), 86_400.0);
        assert_eq!(solve_time_at(NOW, b"1j"), 86_400.0);
        assert_eq!(solve_time_at(NOW, b"30s"), 30.0);
        assert_eq!(solve_time_at(NOW, b"2n"), 120.0);
        assert_eq!(solve_time_at(NOW, b"2mn"), 120.0);
        // one month from September 27: 30 days
        assert_eq!(solve_time_at(NOW, b"1m"), 30.0 * 86_400.0);
        // 2027 is not a leap year
        assert_eq!(solve_time_at(NOW, b"1y"), 365.0 * 86_400.0);
        assert_eq!(solve_time_at(NOW, b"1a"), 365.0 * 86_400.0);
        // pieces add up; zero values and unknown units count for nothing
        assert_eq!(solve_time_at(NOW, b"1h30mn"), 5400.0);
        assert_eq!(solve_time_at(NOW, b"abc"), 0.0);
        assert_eq!(solve_time_at(NOW, b"0h"), 0.0);
        assert_eq!(solve_time_at(NOW, b"5x"), 0.0);
        assert_eq!(solve_time_at(NOW, b"1h-1h"), 0.0);
        // the rAthena help example: +1m-2mn1s-6y
        let t = solve_time_at(NOW, b"+1m-2mn1s-6y");
        assert!(t < 0.0);
    }

    #[test]
    fn solve_time_overflow_is_int_min() {
        // 100 years do not fit an int32 of seconds: the cast gives INT_MIN (a negative time)
        let t = solve_time_at(NOW, b"100y");
        assert_eq!(crate::atcommand_atcmd::c_double_to_i32(t), i32::MIN);
    }

    #[test]
    fn mktime_carries() {
        // January 31 + 1 month = March 3 (2027: February has 28 days)
        let mut tm = localtime(NOW);
        tm.year = 127;
        tm.mon = 0;
        tm.mday = 31;
        let jan31 = mktime(&tm);
        tm.mon = 1;
        let feb31 = mktime(&tm);
        assert_eq!(feb31 - jan31, 31 * 86_400);
        let back = localtime(feb31);
        assert_eq!((back.mon + 1, back.mday), (3, 3));
        // negative months borrow from the year
        tm.mon = -1;
        tm.mday = 1;
        let l = localtime(mktime(&tm));
        assert_eq!((l.year + 1900, l.mon + 1, l.mday), (2026, 12, 1));
    }

    #[test]
    fn email_check() {
        assert!(e_mail_check(b"a@a.com"));
        assert!(e_mail_check(b"gm@loki.test"));
        assert!(!e_mail_check(b"a@"));
        assert!(!e_mail_check(b"ab"));
        assert!(!e_mail_check(b"abc"));
        assert!(!e_mail_check(b"a@b.com."));
        assert!(!e_mail_check(b"a@.com"));
        assert!(!e_mail_check(b"a@b..com"));
        assert!(!e_mail_check(b"a@b;c.com"));
        assert!(!e_mail_check(b"a@b\x01.com"));
        // only the part from the last '@' is checked for '..'
        assert!(e_mail_check(b"a..b@c.com"));
        assert!(!e_mail_check(&[b'a'; 40]));
        let mut long = vec![b'a'; 33];
        long.extend_from_slice(b"@b.com");
        assert_eq!(long.len(), 39);
        assert!(e_mail_check(&long));
    }

    #[test]
    fn printf() {
        assert_eq!(csprintf("Login-serv has been asked to %s the player '%.*s'.", &[A::S(b"ban"), A::D(24), A::S(b"AC Tester")]), "Login-serv has been asked to ban the player 'AC Tester'.");
        assert_eq!(csprintf("'%.*s'", &[A::D(3), A::S(b"abcdef")]), "'abc'");
        assert_eq!(csprintf("Invalid time for %s command (time=%d).", &[A::S(b"@ban"), A::D(0)]), "Invalid time for @ban command (time=0).");
        assert_eq!(csprintf("%d%%", &[A::D(-5)]), "-5%");
        assert_eq!(csprintf("%u", &[A::D(-1)]), "4294967295");
        assert_eq!(csprintf("[%5d|%-3s|%03d]", &[A::D(42), A::S(b"x"), A::D(-7)]), "[   42|x  |-07]");
        assert_eq!(csprintf("%s", &[A::S(b"ab\0cd")]), "ab");
    }

    #[test]
    fn like() {
        assert!(sql_like(b"AC Tester", b"AC_%"));
        assert!(sql_like(b"AC GM", b"ac_%"));
        assert!(sql_like(b"AC Tester", b"%tester"));
        assert!(sql_like(b"AC Tester", b"ac tester"));
        assert!(!sql_like(b"AC Tester", b"AC"));
        assert!(!sql_like(b"AC Tester", b"AC Tester "));
        assert!(sql_like(b"100%", b"100\\%"));
        assert!(!sql_like(b"1000", b"100\\%"));
        assert!(sql_like(b"a_b", b"a\\_b"));
        assert!(!sql_like(b"axb", b"a\\_b"));
        assert!(sql_like(b"", b"%"));
        assert!(!sql_like(b"", b"_"));
        assert!(sql_like(b"\xc9t\xe9", b"\xe9T\xc9"));
    }

    #[test]
    fn char_job_names() {
        use crate::db::jobs::ids::*;
        assert_eq!(char_job_name(JOB_NOVICE), "Novice");
        assert_eq!(char_job_name(JOB_SWORDMAN), "Swordsman");
        assert_eq!(char_job_name(JOB_KNIGHT2), "Knight");
        assert_eq!(char_job_name(JOB_DANCER), "Dancer");
        assert_eq!(char_job_name(JOB_SUMMER2), "Summer");
        // rAthena's numbering quirk: 105 - JOB_HANBOK + JOB_OKTOBERFEST is 106
        assert_eq!(char_job_name(JOB_HANBOK), "Hanbok");
        assert_eq!(char_job_name(JOB_OKTOBERFEST), "Rebellion");
        assert_eq!(char_job_name(JOB_GYPSY), "Gypsy");
        assert_eq!(char_job_name(JOB_BABY_DANCER), "Baby Dancer");
        assert_eq!(char_job_name(JOB_SHADOW_CHASER_T), "Shadow Chaser");
        assert_eq!(char_job_name(JOB_BABY_SHADOW_CHASER), "Baby Shadow Chaser");
        assert_eq!(char_job_name(JOB_OBORO), "Oboro");
        assert_eq!(char_job_name(JOB_SUMMONER), "Summoner");
        assert_eq!(char_job_name(JOB_BABY_REBELLION), "Baby Rebellion");
        assert_eq!(char_job_name(JOB_TROUVERE), "Trouvere");
        assert_eq!(char_job_name(JOB_SPIRIT_HANDLER), "Spirit Handler");
        assert_eq!(char_job_name(JOB_SKY_EMPEROR2), "Sky Emperor");
        // rAthena's numbering quirk in the Star Emperor group
        assert_eq!(char_job_name(JOB_STAR_EMPEROR), "Baby Soul Linker");
        assert_eq!(char_job_name(JOB_BABY_SOUL_REAPER), "Star Emperor");
        assert_eq!(char_job_name(99_999), "Unknown Job");
    }

    #[test]
    fn messages() {
        // map_msg.conf keeps the trailing space of 423 (sscanf "%511[^\r\n]")
        assert_eq!(msg_txt(423), "Your %s has been banished until %s ");
        // wave 28 (wtexts): the text overhaul rewords 424 (rAthena's "Login-serv has been asked to %s the player '%.*s'."); what
        // the C++ relies on is its two conversions in order, %s (the action) then %.*s (NAME_LENGTH, the name): chrif.cpp:936-940
        let m424 = msg_txt(424);
        assert!(m424 != "??" && m424.matches('%').count() == 2, "{m424}");
        assert!(m424.find("%s").unwrap() < m424.find("%.*s").unwrap(), "{m424}");
        assert_eq!(msg_txt(428), "block");
        assert_eq!(msg_txt(432), "change the sex of");
        assert_eq!(msg_txt(state_msg(5)), "Your account has been blocked by the GM Team.");
        assert_eq!(state_msg(100), 421);
        assert_eq!(state_msg(0), 420);
        assert_eq!(state_msg(10), 420);
    }

    #[test]
    fn names() {
        assert_eq!(name24(b"abcdefghijklmnopqrstuvwxyz"), b"abcdefghijklmnopqrstuvw".to_vec());
        assert_eq!(name24(b"ab\0cd"), b"ab".to_vec());
        assert_eq!(scan_name("AC Tester  "), Some("AC Tester  ".to_string()));
        assert_eq!(scan_name(""), None);
        assert_eq!(scan_time_name("1h AC Tester"), Some(("1h".to_string(), "AC Tester".to_string())));
        assert_eq!(scan_time_name("1h"), None);
        assert_eq!(scan_time_name("1h   "), None);
    }
}
