//! Accounts: what rAthena's login server does (login/login.cpp, account.cpp), inside the database.
//!
//! A SpacetimeDB client already has an identity (its token, kept by Loki in PlayerPrefs). `login` binds that identity to
//! an account after checking the password; the binding stays until `logout`, so a returning client is recognised
//! without typing the password again. Passwords are stored as Argon2id PHC strings, never in the clear.
//!
//! Failed logins (wave H, haccount): rAthena counts them per source and refuses the source, never the account
//! (login/ipban.cpp, [`LoginSource`]); the account's own counter is Loki's addition against a caller that takes a new
//! identity for every guess, and it never stops an identity the account already trusts ([`AccountDevice`]).
//!
//! Refusals (wave I, ionline): where rAthena refuses with a packet and a code, the text of the refusal (a reducer's
//! `Err`, or the `my_login_notice` row) starts with both: `#login:<error>` (AC_REFUSE_LOGIN), `#ban:<result>`
//! (SC_NOTIFY_BAN), `#enter:<error>` (HC_REFUSE_ENTER); see [`refuse_login`]. A refusal rAthena has no code for (the
//! account lock, the client hash's format, a password change) stays a plain text.

use argon2::password_hash::{PasswordHash, PasswordHasher, PasswordVerifier, SaltString};
use argon2::Argon2;
use spacetimedb::{reducer, table, view, ConnectionId, Identity, ReducerContext, SpacetimeType, Table, TimeDuration, Timestamp, ViewContext};

use crate::rules;

/// rAthena `login` table (sql-files/main.sql), minus what the module has no use for (email, IP, birth date).
#[table(accessor = account)]
pub struct Account {
    #[primary_key]
    pub id: u32,
    /// As typed at registration.
    pub username: String,
    /// Lower-case copy: account names are unique and looked up without regard to case, as MySQL's default collation
    /// makes rAthena's `WHERE userid = '...'` behave.
    #[unique]
    pub username_key: String,
    /// Argon2id PHC string.
    pub pass_hash: String,
    /// rAthena e_sex: 0 female, 1 male.
    pub sex: u8,
    /// rAthena `group_id` (conf/groups.yml): 0 player, 99 admin. Set by the database owner with SQL.
    pub group_id: u8,
    pub created: Timestamp,
    pub last_login: Option<Timestamp>,
    pub fail_count: u32,
    pub fail_since: Option<Timestamp>,
    pub locked_until: Option<Timestamp>,
}

/// Which client identity is logged in as which account.
#[table(accessor = session)]
pub struct Session {
    #[primary_key]
    pub identity: Identity,
    #[index(btree)]
    pub account_id: u32,
    pub since: Timestamp,
    /// The character picked on the character screen (phase 1 puts it on its map).
    pub selected_char: Option<u32>,
    /// Is this identity connected right now.
    pub online: bool,
    /// The identity's newest connection (None: a row from before this column's meaning, or a call without a
    /// connection). A reconnecting client can be back before the server notices that its old connection died; that
    /// old connection's disconnect must then change nothing ([`stale_disconnect`]).
    pub connection: Option<ConnectionId>,
}

/// What a client may see of its own account.
#[derive(SpacetimeType)]
pub struct MyAccount {
    pub id: u32,
    pub username: String,
    pub sex: u8,
    pub group_id: u8,
    pub char_slots: u8,
    pub selected_char: Option<u32>,
}

#[view(accessor = my_account, public)]
pub fn my_account(ctx: &ViewContext) -> Option<MyAccount> {
    let login = ctx.db.session().identity().find(ctx.sender())?;
    let acc = ctx.db.account().id().find(login.account_id)?;
    // the group the account plays with: the administrator the owner named is 99 on a release server (release.rs)
    use crate::release::server_config__view;
    let group_id = match ctx.db.server_config().key().find(0) {
        Some(cfg) if cfg.mode == crate::release::MODE_RELEASE && !cfg.admin_account.is_empty() && cfg.admin_account == acc.username_key => 99,
        _ => acc.group_id,
    };
    Some(MyAccount { id: acc.id, username: acc.username, sex: acc.sex, group_id, char_slots: rules::CHAR_SLOTS, selected_char: login.selected_char })
}

/// The account the caller is logged in as, or an error for the login screen.
pub fn current_account(ctx: &ReducerContext) -> Result<Account, String> {
    // a saved identity reaches the character screen without `login`: the version handshake is checked here too
    crate::release::check_client(ctx)?;
    let login = ctx.db.session().identity().find(ctx.sender()).ok_or("You are not logged in.")?;
    ctx.db.account().id().find(login.account_id).ok_or_else(|| "Your account no longer exists.".to_string())
}

// ---------------------------------------------------------------------------------------------- refusal codes

/// AC_REFUSE_LOGIN 0x083e as text, for a client that shows its own message per code: "#login:<error> <text>".
/// `error` is the packet's (loginclif.cpp:171-205 lists them; logclif_auth_failed :207-233 sends what login.cpp
/// login_mmo_auth :296-432 returned: 0 unregistered id, 1 incorrect password, 2 expired, 3 rejected, 5 the client is not
/// the latest, 6 banned until, else the account's `state - 1`); `text` says the same in English for a client that
/// knows no codes (the test rigs), and is the whole message where the module knows more than the code says.
pub fn refuse_login(error: u32, text: &str) -> String {
    format!("#login:{error} {text}")
}

/// AC_REFUSE_LOGIN with its `unblock_time` (error 6, loginclif.cpp:224-231: the account's `unban_time` through
/// timestamp2string with login_config.date_format): "#login:6[<unblock_time>] <text>".
pub fn refuse_login_until(unblock_time: &str, text: &str) -> String {
    format!("#login:6[{unblock_time}] {text}")
}

/// SC_NOTIFY_BAN 0x0081 from the login / char server as text (char_clif.cpp:492-520 chclif_send_auth_result,
/// loginclif.cpp logclif_sent_auth_result): "#ban:<result> <text>".
pub fn notify_ban(result: u8, text: &str) -> String {
    format!("#ban:{result} {text}")
}

/// HC_REFUSE_ENTER 0x006c as text (char_clif.cpp:1585-1592 chclif_reject): "#enter:<error> <text>".
pub fn refuse_enter(error: u8, text: &str) -> String {
    format!("#enter:{error} {text}")
}

/// The character deletion's refusals as text, with the packet's result (char_clif.cpp:522-585):
/// "#delres:<result> <text>" (HC_DELETE_CHAR3_RESERVED 0x0828, chclif_char_delete2_ack: 0 already queued, 3 not found,
/// 4 in a guild, 5 in a party), "#delete:<result> <text>" (HC_DELETE_CHAR3 0x082a, chclif_char_delete2_accept_ack:
/// 2 the server's rules, 3 not found, 4 not queued or not yet due, 5 the birth date does not match),
/// "#delcancel:<result> <text>" (HC_DELETE_CHAR3_CANCEL 0x082c, chclif_char_delete2_cancel_ack: 2 not found).
pub fn refuse_delete(packet: &str, result: u8, text: &str) -> String {
    format!("#{packet}:{result} {text}")
}

fn check_name(username: &str) -> Result<(), String> {
    let n = username.chars().count();
    if n < rules::ACCOUNT_NAME_MIN || n > rules::NAME_MAX {
        // login.cpp login_mmo_auth_new:238-239 `strlen(userid) < login_config.acc_name_min_length` -> 1 (rAthena has
        // no upper test: the packet field holds 23 characters)
        return Err(refuse_login(1, &format!("Account names are {} to {} characters long.", rules::ACCOUNT_NAME_MIN, rules::NAME_MAX)));
    }
    if username.chars().any(|c| c.is_control()) {
        return Err("Account names can't contain control characters.".into());
    }
    Ok(())
}

/// The client never sends the password itself: it sends hex(SHA-256("loki-online:" + lower-case account name + ":" +
/// password)), so a connection without TLS (a local `ws://` server) doesn't reveal the password a player may use
/// elsewhere. The client enforces the password length rule (rules::PASSWORD_MIN..=PASSWORD_MAX) before hashing.
fn check_client_hash(client_hash: &str) -> Result<(), String> {
    if client_hash.len() != 64 || !client_hash.bytes().all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b)) {
        return Err("This client is too old for Loki Online (the password must be sent hashed).".into());
    }
    Ok(())
}

fn hash_password(ctx: &ReducerContext, username_key: &str, password: &str) -> Result<String, String> {
    // The reducer RNG is seeded from the call's timestamp: fine for a salt, which must be unique, not secret.
    // The account name is mixed in so two registrations can never share a salt.
    let mut salt = [0u8; 16];
    for (i, b) in salt.iter_mut().enumerate() {
        *b = ctx.random::<u8>() ^ username_key.as_bytes().get(i).copied().unwrap_or(0x5a);
    }
    let salt = SaltString::encode_b64(&salt).map_err(|e| format!("salt: {e}"))?;
    Argon2::default().hash_password(password.as_bytes(), &salt).map(|h| h.to_string()).map_err(|e| format!("hash: {e}"))
}

/// One identity is one connection: a client that logs in again, or out, while its character is in the world has
/// closed its map-server link — the character leaves through clif_quitsave (map_quit, or clif_delayquit while
/// prevent_logout holds), as when rAthena's socket closes. rAthena never sees this: a new login there is a new socket.
fn leave_world(ctx: &ReducerContext) {
    crate::world::run(ctx, |c| crate::pc::on_client_gone(c, c.sender()));
}

fn bind(ctx: &ReducerContext, account_id: u32) {
    leave_world(ctx);
    trust_device(ctx, account_id);
    let row = Session { identity: ctx.sender(), account_id, since: ctx.timestamp, selected_char: None, online: true, connection: ctx.connection_id() };
    if ctx.db.session().identity().find(ctx.sender()).is_some() {
        ctx.db.session().identity().update(row);
    } else {
        ctx.db.session().insert(row);
    }
}

// ---------------------------------------------------------------------------------------------- failed logins

/// rAthena's failed-login record of one source: its `loginlog` rows with rcode 0 / 1 inside the interval, and its
/// `ipbanlist` entry. rAthena's source is the IP address (a /24 for the ban: ipban.cpp:83 `'%u.%u.%u.*'`); a reducer
/// sees no address, so the source is the caller's identity.
#[table(accessor = login_source)]
pub struct LoginSource {
    #[primary_key]
    pub identity: Identity,
    /// Unix ms of this source's failed logins inside the interval, oldest first (never more than the limit).
    pub fails: Vec<i64>,
    /// ipbanlist `rtime` (unix ms; 0 none): logins and registrations from this source are refused until then.
    pub banned_until: i64,
    /// Unix ms after which the row means nothing (the lazy ipban_cleanup deletes by it).
    #[index(btree)]
    pub expires: i64,
}

/// ipban.cpp ipban_log (:71-88) with loginlog_failedattempts (loginlog.cpp:35-53): the failure at `now` joins the
/// source's failures of the last `ipban_dynamic_pass_failure_ban_interval` minutes (`time > NOW() - INTERVAL n MINUTE`);
/// when they reach `ipban_dynamic_pass_failure_ban_limit` the source is banned for
/// `ipban_dynamic_pass_failure_ban_duration` minutes. Returns the ban's end (unix ms) when this failure caused one.
pub fn source_failed(fails: &mut Vec<i64>, now: i64) -> Option<i64> {
    let window = rules::LOGIN_FAIL_WINDOW_SECS * 1000;
    fails.retain(|&t| t > now - window);
    fails.push(now);
    let limit = rules::LOGIN_FAIL_LIMIT as usize;
    if fails.len() > limit {
        let extra = fails.len() - limit;
        fails.drain(..extra); // only the newest `limit` can ever matter
    }
    (fails.len() >= limit).then_some(now + rules::LOGIN_LOCK_SECS * 1000)
}

/// ipban.cpp ipban_check (:40-66): an entry whose `rtime` is still to come.
pub fn source_banned(banned_until: i64, now: i64) -> bool {
    banned_until > now
}

fn now_ms(ctx: &ReducerContext) -> i64 {
    ctx.timestamp.to_micros_since_unix_epoch() / 1000
}

/// loginclif.cpp logclif_parse:520-530: a connection from a banned source is refused before it can name an account
/// (`logclif_auth_failed( fd, 3 ); // 3 = Rejected from Server`). Here: before `login`, `register` and
/// `change_password` look at anything.
fn check_source(ctx: &ReducerContext) -> Result<(), String> {
    match ctx.db.login_source().identity().find(ctx.sender()) {
        Some(src) if source_banned(src.banned_until, now_ms(ctx)) => Err(refuse_login(3, SOURCE_BANNED)),
        _ => Ok(()),
    }
}

/// Why refusal 3 came (rAthena's client only has "Rejected from Server"; the module can say more).
pub const SOURCE_BANNED: &str = "Too many failed logins from this PC: rejected from the server for a few minutes.";

/// loginclif.cpp logclif_auth_failed:222-223: a refusal 0 (unregistered id) or 1 (incorrect password) is logged
/// against the source (ipban_log). Also what ipban.cpp ipban_cleanup (:99-108) does every `ipban_cleanup_interval`:
/// rows that can no longer count are deleted, here whenever a failure is recorded instead of by a timer.
fn record_source_failure(ctx: &ReducerContext) {
    let now = now_ms(ctx);
    let stale: Vec<Identity> = ctx.db.login_source().expires().filter(..now).map(|r| r.identity).collect();
    for id in stale {
        ctx.db.login_source().identity().delete(id);
    }
    let old = ctx.db.login_source().identity().find(ctx.sender());
    let mut fails = old.as_ref().map(|r| r.fails.clone()).unwrap_or_default();
    let mut banned_until = old.as_ref().map(|r| r.banned_until).unwrap_or(0);
    if let Some(until) = source_failed(&mut fails, now) {
        banned_until = until;
        log::warn!("login: source {} refused for {} s after {} failed logins", ctx.sender(), rules::LOGIN_LOCK_SECS, fails.len());
    }
    let expires = banned_until.max(now + rules::LOGIN_FAIL_WINDOW_SECS * 1000);
    let row = LoginSource { identity: ctx.sender(), fails, banned_until, expires };
    if old.is_some() {
        ctx.db.login_source().identity().update(row);
    } else {
        ctx.db.login_source().insert(row);
    }
}

/// Loki's own (rAthena has no such list): the identities that have logged in to an account with its password. The
/// account's lock ([`Account::locked_until`]) does not apply to them, so whoever keeps an account locked with wrong
/// passwords cannot keep its owner out of the PCs the owner has played from.
#[table(accessor = account_device)]
pub struct AccountDevice {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    #[index(btree)]
    pub account_id: u32,
    pub identity: Identity,
    pub last: Timestamp,
}

/// How many identities an account remembers (the oldest goes first).
const ACCOUNT_DEVICES_MAX: usize = 8;

fn device_trusted(ctx: &ReducerContext, account_id: u32) -> bool {
    ctx.db.account_device().account_id().filter(account_id).any(|d| d.identity == ctx.sender())
}

fn trust_device(ctx: &ReducerContext, account_id: u32) {
    let mut rows: Vec<AccountDevice> = ctx.db.account_device().account_id().filter(account_id).collect();
    if let Some(d) = rows.iter().find(|d| d.identity == ctx.sender()) {
        ctx.db.account_device().id().update(AccountDevice { id: d.id, account_id, identity: d.identity, last: ctx.timestamp });
        return;
    }
    rows.sort_by_key(|d| d.last);
    while rows.len() >= ACCOUNT_DEVICES_MAX {
        ctx.db.account_device().id().delete(rows.remove(0).id);
    }
    ctx.db.account_device().insert(AccountDevice { id: 0, account_id, identity: ctx.sender(), last: ctx.timestamp });
}

/// Loki's own second guard (an identity is free, an IP address is not: a caller could take a new identity for every
/// guess and never meet [`check_source`]): wrong passwords also count on the account, `LOGIN_FAIL_LIMIT` in
/// `LOGIN_FAIL_WINDOW_SECS` lock it for `LOGIN_LOCK_SECS`, and a locked account refuses, before the password is
/// looked at, every identity it does not trust. Returns whether the account is locked now.
fn account_failed(acc: &mut Account, now: Timestamp) -> bool {
    let window = TimeDuration::from_micros(rules::LOGIN_FAIL_WINDOW_SECS * 1_000_000);
    let fresh = acc.fail_since.map(|t| now > t + window).unwrap_or(true);
    if fresh {
        acc.fail_count = 0;
        acc.fail_since = Some(now);
    }
    acc.fail_count += 1;
    if acc.fail_count >= rules::LOGIN_FAIL_LIMIT {
        acc.locked_until = Some(now + TimeDuration::from_micros(rules::LOGIN_LOCK_SECS * 1_000_000));
        acc.fail_count = 0;
        acc.fail_since = None;
        log::warn!("account {} locked for new PCs after repeated wrong passwords", acc.id);
    }
    account_locked(acc, now)
}

fn account_locked(acc: &Account, now: Timestamp) -> bool {
    acc.locked_until.map(|until| now < until).unwrap_or(false)
}

/// login.cpp login_mmo_auth:343-346 `return 0; // 0 = Unregistered ID` and :353-356 `return 1; // 1 = Incorrect
/// Password`, with the login server's own texts for them (conf/msg_conf/login_msg.conf 0 and 1). (Until wave I both
/// were one text, "so account names can't be probed": rAthena tells the two apart, and so does a registration.)
pub const UNREGISTERED: &str = "Unregistered ID.";
pub const INCORRECT: &str = "Incorrect Password.";
/// Loki's own (rAthena has no account lock, so no code): [`account_failed`].
const LOCKED: &str = "Too many wrong passwords: this account is locked for a few minutes on PCs it has not been played from.";

fn verify_password(acc: &Account, password: &str) -> bool {
    PasswordHash::new(&acc.pass_hash).map(|h| Argon2::default().verify_password(password.as_bytes(), &h).is_ok()).unwrap_or(false)
}

// ---------------------------------------------------------------------------------------------- registration

/// login.cpp login_mmo_auth_new's `static int32 num_regs` and `static t_tick new_reg_tick` (key 0).
#[table(accessor = reg_flood)]
pub struct RegFlood {
    #[primary_key]
    pub key: u8,
    pub num_regs: u32,
    pub new_reg_tick: i64,
}

/// login.cpp login_mmo_auth_new:230-236 "Account Registration Flood Protection": false = refused (result 3,
/// "registration limit exceeded"). `state` is (num_regs, new_reg_tick), `tick` gettick().
pub fn reg_flood_allows(state: &mut (u32, i64), tick: i64) -> bool {
    if state.1 == 0 {
        state.1 = tick;
    }
    !(tick - state.1 < 0 && state.0 >= rules::ALLOWED_REGS)
}

/// login.cpp login_mmo_auth_new:273-277, after the account is made: "Update the registration check."
pub fn reg_flood_registered(state: &mut (u32, i64), tick: i64) {
    if tick - state.1 > 0 {
        state.0 = 0;
        state.1 = tick + rules::TIME_ALLOWED_SECS * 1000;
    }
    state.0 += 1;
}

/// New account (rAthena's `_M` / `_F` auto-registration, login.cpp login_mmo_auth_new): `sex` 0 female, 1 male;
/// `password` is the client hash (see check_client_hash).
/// The caller is logged in as the new account straight away.
#[reducer]
pub fn register(ctx: &ReducerContext, username: String, password: String, sex: u8) -> Result<(), String> {
    crate::release::check_client(ctx)?;
    check_source(ctx)?;
    // conf/login_athena.conf allowed_regs / time_allowed, on a release server (rules::ALLOWED_REGS)
    let release = crate::release::is_release();
    let mut flood = ctx.db.reg_flood().key().find(0).map(|r| (r.num_regs, r.new_reg_tick)).unwrap_or((0, 0));
    // login_mmo_auth_new's results (login.cpp:215-222): 3 registration limit exceeded (:233-236), 1 userid or pass too
    // short (:238-239) or the userid exists (:245-249), 0 the sex is not M / F (:241-243)
    if release && !reg_flood_allows(&mut flood, now_ms(ctx)) {
        log::info!("Account registration denied (registration limit exceeded)");
        return Err(refuse_login(3, "Too many new accounts right now: try again in a few seconds."));
    }
    let username = username.trim().to_string();
    check_name(&username)?;
    check_client_hash(&password)?;
    if sex > 1 {
        return Err(refuse_login(0, "Pick male or female."));
    }
    let key = username.to_lowercase();
    if ctx.db.account().username_key().find(&key).is_some() {
        return Err(refuse_login(1, "That account name is taken."));
    }
    let pass_hash = hash_password(ctx, &key, &password)?;
    let id = crate::ids::next(ctx, "account", rules::START_ACCOUNT_ID);
    ctx.db.account().insert(Account {
        id, username: username.clone(), username_key: key, pass_hash, sex, group_id: 0, created: ctx.timestamp,
        last_login: Some(ctx.timestamp), fail_count: 0, fail_since: None, locked_until: None,
    });
    if release {
        reg_flood_registered(&mut flood, now_ms(ctx));
        let row = RegFlood { key: 0, num_regs: flood.0, new_reg_tick: flood.1 };
        if ctx.db.reg_flood().key().find(0).is_some() {
            ctx.db.reg_flood().key().update(row);
        } else {
            ctx.db.reg_flood().insert(row);
        }
    }
    // the _M / _F registration (login_mmo_auth_new) goes on through login_mmo_auth: the new account has logged in once
    crate::atcommand_atcmd::accounts::login_mmo_auth_accepted(&ctx.db, id);
    bind(ctx, id);
    ctx.db.login_notice().identity().delete(ctx.sender());
    log::info!("account {id} '{username}' registered");
    Ok(())
}

/// Checks the password and logs the caller in (login.cpp login_mmo_auth). A refusal that must leave a trace (a failed
/// attempt is counted) cannot fail the reducer, which would roll the count back: it is written to `my_login_notice`
/// and the reducer succeeds with `my_account` still empty.
#[reducer]
pub fn login(ctx: &ReducerContext, username: String, password: String) -> Result<(), String> {
    crate::release::check_client(ctx)?;
    let key = username.trim().to_lowercase();
    check_client_hash(&password)?;
    check_source(ctx)?;
    // rAthena answers an unknown account with refuse code 0 and a wrong password with 1 (login.cpp:343-356); both
    // count against the source (loginclif.cpp:222-223).
    let Some(mut acc) = ctx.db.account().username_key().find(&key) else {
        record_source_failure(ctx);
        login_refused(ctx, &refuse_login(0, UNREGISTERED));
        return Ok(());
    };
    // Loki's account lock stops only the identities the account does not know: its owner's PCs go on to the password
    if account_locked(&acc, ctx.timestamp) && !device_trusted(ctx, acc.id) {
        record_source_failure(ctx);
        login_refused(ctx, LOCKED);
        return Ok(());
    }
    if !verify_password(&acc, &password) {
        record_source_failure(ctx);
        let trusted = device_trusted(ctx, acc.id);
        let locked = account_failed(&mut acc, ctx.timestamp);
        ctx.db.account().id().update(acc);
        login_refused(ctx, &if locked && !trusted { LOCKED.to_string() } else { refuse_login(1, INCORRECT) });
        return Ok(());
    }
    // login.cpp login_mmo_auth:365-375: an account banned (@ban: unban_time to come) or blocked (@block: state != 0)
    // is refused (atcmd agent: atcommand_accounts.rs)
    if let Some(refusal) = crate::atcommand_atcmd::accounts::login_mmo_auth_check(&ctx.db, acc.id, ctx.timestamp) {
        login_refused(ctx, &refusal);
        return Ok(());
    }
    acc.fail_count = 0;
    acc.fail_since = None;
    acc.locked_until = None;
    acc.last_login = Some(ctx.timestamp);
    let id = acc.id;
    ctx.db.account().id().update(acc);
    crate::atcommand_atcmd::accounts::login_mmo_auth_accepted(&ctx.db, id); // login.cpp:419-424 unban_time = 0, logincount++
    bind(ctx, id);
    ctx.db.login_notice().identity().delete(ctx.sender());
    Ok(())
}

/// Loki's own (rAthena e985006 changes a password only in its `login` table, by SQL or a control panel): the logged-in
/// account's password, proven with the current one. Both are client hashes (check_client_hash). A wrong current
/// password counts as a failed login of this source and is answered through `my_login_notice`, like `login`. Every
/// other PC's saved login to the account ends (its character leaves the world), and only this PC stays trusted: a
/// password is changed because someone else may know the old one.
#[reducer]
pub fn change_password(ctx: &ReducerContext, old_password: String, new_password: String) -> Result<(), String> {
    let mut acc = current_account(ctx)?;
    check_client_hash(&old_password)?;
    check_client_hash(&new_password)?;
    check_source(ctx)?;
    if !verify_password(&acc, &old_password) {
        record_source_failure(ctx);
        login_refused(ctx, "The current password is wrong.");
        return Ok(());
    }
    if old_password == new_password {
        return Err("The new password is the same as the current one.".into());
    }
    acc.pass_hash = hash_password(ctx, &acc.username_key, &new_password)?;
    acc.fail_count = 0;
    acc.fail_since = None;
    acc.locked_until = None;
    let id = acc.id;
    ctx.db.account().id().update(acc);
    let me = ctx.sender();
    // the account's character in the world on another PC leaves as when someone else logs in (reason 2)
    crate::world::run(ctx, |c| {
        if crate::pc::get(c, id).map(|p| p.identity != me).unwrap_or(false) {
            crate::pc::disconnectplayer(c, id, 2);
        }
    });
    let others: Vec<Identity> = ctx.db.session().account_id().filter(id).map(|s| s.identity).filter(|i| *i != me).collect();
    for other in others {
        ctx.db.session().identity().delete(other);
    }
    let devices: Vec<u64> = ctx.db.account_device().account_id().filter(id).filter(|d| d.identity != me).map(|d| d.id).collect();
    for d in devices {
        ctx.db.account_device().id().delete(d);
    }
    ctx.db.login_notice().identity().delete(me);
    log::info!("account {id} changed its password");
    Ok(())
}

/// Loki's own (on rAthena an administrator writes `login`.`user_pass` by SQL): the owner gives an account a new
/// password, for a player who forgot theirs. `new_password` is the client hash of the new password for that account
/// name (hex(SHA-256("loki-online:" + lower-case name + ":" + password)): `acctest --hash <name> <password>` prints
/// it), since the module never sees a password. Every saved login to the account ends, its character leaves the world,
/// the lock and the trusted PCs are cleared.
#[reducer]
pub fn owner_reset_password(ctx: &ReducerContext, username: String, new_password: String) -> Result<(), String> {
    crate::release::require_owner(ctx, "reset a password")?;
    check_client_hash(&new_password)?;
    let key = username.trim().to_lowercase();
    let Some(mut acc) = ctx.db.account().username_key().find(&key) else { return Err(format!("There is no account '{key}'.")) };
    acc.pass_hash = hash_password(ctx, &key, &new_password)?;
    acc.fail_count = 0;
    acc.fail_since = None;
    acc.locked_until = None;
    let id = acc.id;
    ctx.db.account().id().update(acc);
    crate::world::run(ctx, |c| crate::pc::disconnectplayer(c, id, 2));
    let sessions: Vec<Identity> = ctx.db.session().account_id().filter(id).map(|s| s.identity).collect();
    for s in sessions {
        ctx.db.session().identity().delete(s);
    }
    let devices: Vec<u64> = ctx.db.account_device().account_id().filter(id).map(|d| d.id).collect();
    for d in devices {
        ctx.db.account_device().id().delete(d);
    }
    log::info!("account {id} '{key}': password reset by the owner");
    Ok(())
}

/// Why this client's last login attempt was refused (kept per identity; cleared by a successful login, and when the
/// identity disconnects or connects: a notice answers a call of the connection that made it, nothing else). A wrong
/// password can't simply fail the reducer: that would roll back the failure counter with the transaction.
#[table(accessor = login_notice)]
pub struct LoginNotice {
    #[primary_key]
    pub identity: Identity,
    pub message: String,
    pub at: Timestamp,
}

#[view(accessor = my_login_notice, public)]
pub fn my_login_notice(ctx: &ViewContext) -> Option<LoginNotice> {
    ctx.db.login_notice().identity().find(ctx.sender())
}

pub fn login_refused(ctx: &ReducerContext, message: &str) {
    login_refused_for(ctx, ctx.sender(), message);
}

pub fn login_refused_for(ctx: &ReducerContext, identity: Identity, message: &str) {
    let row = LoginNotice { identity, message: message.to_string(), at: ctx.timestamp };
    if ctx.db.login_notice().identity().find(identity).is_some() {
        ctx.db.login_notice().identity().update(row);
    } else {
        ctx.db.login_notice().insert(row);
    }
}

/// Forget this client's login (the next start shows the login screen).
#[reducer]
pub fn logout(ctx: &ReducerContext) {
    leave_world(ctx);
    ctx.db.session().identity().delete(ctx.sender());
}

pub fn on_connect(ctx: &ReducerContext) {
    // a notice left by a connection whose disconnect never ran
    ctx.db.login_notice().identity().delete(ctx.sender());
    if let Some(l) = ctx.db.session().identity().find(ctx.sender()) {
        ctx.db.session().identity().update(Session { online: true, connection: ctx.connection_id(), ..l });
    }
}

/// Is the connection that just closed an older one of an identity that has connected again since? (Loki's own:
/// rAthena's sockets have no such thing, each connection is its own session.) Its close is then no event at all: the
/// character in the world, the selection and the online flag belong to the newer connection.
pub fn stale_disconnect(ctx: &ReducerContext) -> bool {
    match (ctx.db.session().identity().find(ctx.sender()).and_then(|s| s.connection), ctx.connection_id()) {
        (Some(current), Some(closing)) => current != closing,
        _ => false,
    }
}

pub fn on_disconnect(ctx: &ReducerContext) {
    ctx.db.login_notice().identity().delete(ctx.sender());
    crate::release::on_disconnect(ctx);
    if let Some(l) = ctx.db.session().identity().find(ctx.sender()) {
        // (no newest connection any more: whichever connection of this identity closes next is handled as its own)
        ctx.db.session().identity().update(Session { online: false, selected_char: None, connection: None, ..l });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const MIN: i64 = 60_000;

    /// The refusals that carry rAthena's packet and code (wave I): what the client parses (Net/Stdb/StdbSession.cs
    /// `Coded`) and what a client without the table reads.
    #[test]
    fn refusals_carry_rathenas_codes() {
        // login.cpp login_mmo_auth: 0 unregistered id, 1 incorrect password; loginclif.cpp:525 3 rejected
        assert_eq!(refuse_login(0, UNREGISTERED), "#login:0 Unregistered ID.");
        assert_eq!(refuse_login(1, INCORRECT), "#login:1 Incorrect Password.");
        assert!(refuse_login(3, SOURCE_BANNED).starts_with("#login:3 Too many failed logins"));
        // 6 with the packet's unblock_time (date_format "%Y-%m-%d %H:%M:%S": 19 characters in unblock_time[20])
        assert_eq!(refuse_login_until("2026-10-07 12:00:00", "You are prohibited to log in until 2026-10-07 12:00:00."), "#login:6[2026-10-07 12:00:00] You are prohibited to log in until 2026-10-07 12:00:00.");
        // release.rs: the handshake's refusal is 5
        assert!(crate::release::TOO_OLD.starts_with("#login:5 "));
        // char.cpp char_auth_ok: SC_NOTIFY_BAN 8; char_clif.cpp chclif_reject: HC_REFUSE_ENTER 0
        assert_eq!(notify_ban(8, "Server still recognizes your last login."), "#ban:8 Server still recognizes your last login.");
        assert_eq!(refuse_enter(0, "Rejected from server."), "#enter:0 Rejected from server.");
        assert_eq!(refuse_delete("delres", 4, "Leave your guild before deleting this character."), "#delres:4 Leave your guild before deleting this character.");
        // the account lock is Loki's own: no code
        assert!(!LOCKED.starts_with('#'));
    }

    /// ipban.cpp ipban_log + loginlog_failedattempts with conf/login_athena.conf's 7 failures / 5 minutes / 5 minutes.
    #[test]
    fn seventh_failure_inside_five_minutes_bans_the_source() {
        let mut fails = Vec::new();
        let t0 = 1_000_000_000;
        for i in 0..6 {
            assert_eq!(source_failed(&mut fails, t0 + i * 1000), None, "failure {}", i + 1);
        }
        assert_eq!(source_failed(&mut fails, t0 + 6000), Some(t0 + 6000 + 5 * MIN));
        assert!(source_banned(t0 + 6000 + 5 * MIN, t0 + 6000 + 5 * MIN - 1));
        assert!(!source_banned(t0 + 6000 + 5 * MIN, t0 + 6000 + 5 * MIN)); // rtime > NOW()
    }

    /// `time > NOW() - INTERVAL 5 MINUTE`: failures older than the interval no longer count (a sliding window).
    #[test]
    fn failures_older_than_the_interval_do_not_count() {
        let mut fails = Vec::new();
        let t0 = 1_000_000_000;
        for i in 0..6 {
            assert_eq!(source_failed(&mut fails, t0 + i * 1000), None);
        }
        // the seventh comes when the first is exactly five minutes old: that one is out, six remain
        assert_eq!(source_failed(&mut fails, t0 + 5 * MIN), None);
        assert_eq!(fails.len(), 6);
        // one more inside the window of the remaining six: seven
        assert_eq!(source_failed(&mut fails, t0 + 5 * MIN + 500), Some(t0 + 5 * MIN + 500 + 5 * MIN));
        // one failure a minute never reaches seven in five minutes
        let mut slow = Vec::new();
        for i in 0..30 {
            assert_eq!(source_failed(&mut slow, t0 + i * MIN), None);
        }
    }

    /// login.cpp login_mmo_auth_new: one registration per 10 s once the window is set; the static counters start at
    /// zero, which lets the first two through (the first leaves `new_reg_tick` at its own tick: DIFF_TICK is 0, not > 0).
    #[test]
    fn registration_flood_limit_is_rathenas() {
        let mut st = (0u32, 0i64);
        let t0 = 50_000;
        assert!(reg_flood_allows(&mut st, t0));
        reg_flood_registered(&mut st, t0);
        assert_eq!(st, (1, t0));
        // the second, a second later: the window had not started (tick is past new_reg_tick), so it is allowed and starts it
        assert!(reg_flood_allows(&mut st, t0 + 1000));
        reg_flood_registered(&mut st, t0 + 1000);
        assert_eq!(st, (1, t0 + 1000 + 10_000));
        // inside the 10 s: refused
        assert!(!reg_flood_allows(&mut st, t0 + 2000));
        assert!(!reg_flood_allows(&mut st, t0 + 10_999));
        // at the window's end (DIFF_TICK == 0 is not < 0): allowed, but the window is not renewed (not > 0)
        assert!(reg_flood_allows(&mut st, t0 + 11_000));
        reg_flood_registered(&mut st, t0 + 11_000);
        assert_eq!(st, (2, t0 + 11_000));
        // after it: allowed, and a new window starts
        assert!(reg_flood_allows(&mut st, t0 + 11_001));
        reg_flood_registered(&mut st, t0 + 11_001);
        assert_eq!(st, (1, t0 + 21_001));
        assert!(!reg_flood_allows(&mut st, t0 + 21_000));
    }
}
