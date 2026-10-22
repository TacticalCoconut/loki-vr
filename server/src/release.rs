//! The release switch and the version handshake. Both are Loki's own: rAthena has neither as code (its equivalents are
//! the conf files an administrator edits, and a PACKETVER both sides are compiled with).
//!
//! **The release switch** is one row, `server_config` (key 0), that only the database's owner can write:
//! - `mode` 0, "test" (the default, and what every database published before this row existed runs as): Loki's test
//!   overlay rules. Group 0 is level 99 with every command (data/overlay/conf/import/groups.yml), `@lokirnd` and the
//!   `debug_rng` row fix every roll, the test NPC files of npc/scripts_custom.conf are loaded, accounts register without
//!   a flood limit, a client needs no version handshake. Every parity suite and smoke runs in this mode.
//! - `mode` 1, "release": rAthena's stock rules. The groups are rAthena's own conf/groups.yml (a player has
//!   `@changedress` and `@resurrect` and nothing else; [`crate::rules::groups`]); the account the owner names in
//!   `admin_account` is group 99 (Admin), as `UPDATE login SET group_id = 99` makes one on rAthena; `@lokirnd` is no
//!   command and a `debug_rng` row is ignored (world.rs); the files of npc/custom/ are not loaded, as stock
//!   npc/scripts_custom.conf loads none (the world is rebuilt when the mode changes: npc.rs ensure_world); rAthena's
//!   registration flood limit applies (account.rs); a client must have said `hello` with a protocol the module accepts.
//!
//! How the owner sets it: `spacetime call <db> set_server_mode release <admin account>` (or `test ""`), accepted only
//! from the identity that published the database (recorded by `init` in `server_owner`). The row can also be written
//! with SQL, which SpacetimeDB allows on a private table to the owner alone:
//! `UPDATE server_config SET mode = 1, admin_account = 'name' WHERE key = 0` (`INSERT INTO server_config (key, mode,
//! admin_account) VALUES (0, 1, 'name')` on a database updated in place, whose `init` never ran with this code).
//! Every rule reads the row at the start of each reducer call ([`load`]), so both ways take effect at once.
//!
//! What the switch does NOT change (compile-time data; listed in server/ARCHITECTURE.md): the battle conf overlay
//! (base / job EXP x5, sit regeneration x25), db/import (attendance, instance_db, item_cash).

use std::cell::Cell;

use spacetimedb::{reducer, table, view, AnonymousViewContext, ConnectionId, Identity, ReducerContext, SpacetimeType, Table, Timestamp};

use crate::account::account;

pub const MODE_TEST: u8 = 0;
pub const MODE_RELEASE: u8 = 1;

/// The one setting (key 0). Private: clients see only what [`server_info`] shows.
#[table(accessor = server_config)]
pub struct ServerConfig {
    #[primary_key]
    pub key: u8,
    /// [`MODE_TEST`] or [`MODE_RELEASE`].
    pub mode: u8,
    /// Lower-case account name (`Account::username_key`) of the administrator in release mode; "" for none.
    pub admin_account: String,
}

/// Who published the database (key 0), written by `init`: the only identity `set_server_mode` listens to.
#[table(accessor = server_owner)]
pub struct ServerOwner {
    #[primary_key]
    pub key: u8,
    pub identity: Identity,
}

thread_local! {
    /// This reducer call's mode, for the rules that have no database handle (rules::group and the data lookups).
    static RELEASE: Cell<bool> = const { Cell::new(false) };
}

/// Reads the setting for this reducer call (world.rs Ctx::new, and the account reducers that run without a Ctx).
pub fn load(db: &spacetimedb::Local) -> bool {
    let release = db.server_config().key().find(0).map(|r| r.mode == MODE_RELEASE).unwrap_or(false);
    RELEASE.with(|r| r.set(release));
    release
}

/// Is this call running under release rules ([`load`] ran at its start).
pub fn is_release() -> bool {
    RELEASE.with(|r| r.get())
}

#[cfg(test)]
pub fn set_for_test(release: bool) {
    RELEASE.with(|r| r.set(release));
}

/// Loki's test-rig commands, which are no commands at all on a release server (data.rs AtCommands::info,
/// rules::can_use_command): `@lokirnd` (the oracle's patch; not in rAthena's atcommand.cpp).
pub fn hides_command(name: &str) -> bool {
    is_release() && name.eq_ignore_ascii_case("lokirnd")
}

/// The NPC files a release server does not load at start-up: the test NPCs of npc/custom/ (while keeping
/// Loki's gameplay helpers loki_codex.txt and loki_tutorial.txt).
pub fn skips_npc_file(path: &str) -> bool {
    is_release() && path.starts_with("npc/custom/") && path != "npc/custom/loki_codex.txt" && path != "npc/custom/loki_tutorial.txt"
}

/// Mixed into the world's build key (npc.rs ensure_world): a change of mode changes the start-up NPC set, so the
/// world is built again. 0 in test mode: the key of every existing database stays what it was.
pub fn world_key() -> u64 {
    if is_release() { 0x4c4f_4b49_5245_4c31 } else { 0 }
}

/// The group an account plays with: its `group_id` (rAthena's `login`.`group_id`), or 99 for the administrator the
/// owner named, in release mode.
pub fn effective_group(db: &spacetimedb::Local, acc: &crate::account::Account) -> u8 {
    match db.server_config().key().find(0) {
        Some(cfg) if cfg.mode == MODE_RELEASE && !cfg.admin_account.is_empty() && cfg.admin_account == acc.username_key => 99,
        _ => acc.group_id,
    }
}

/// The caller must be the identity that published the database (`server_owner`).
pub fn require_owner(ctx: &ReducerContext, what: &str) -> Result<(), String> {
    match ctx.db.server_owner().key().find(0) {
        Some(o) if o.identity == ctx.sender() => Ok(()),
        Some(_) => Err(format!("Only the database's owner may {what}.")),
        None => Err("This database has no recorded owner (it was updated in place): its owner writes the tables with SQL.".into()),
    }
}

/// `init`: the publisher is the owner; the setting starts as "test".
pub fn on_init(ctx: &ReducerContext) {
    if ctx.db.server_owner().key().find(0).is_none() {
        ctx.db.server_owner().insert(ServerOwner { key: 0, identity: ctx.sender() });
        log::info!("server owner: {}", ctx.sender());
    }
    if ctx.db.server_config().key().find(0).is_none() {
        ctx.db.server_config().insert(ServerConfig { key: 0, mode: MODE_TEST, admin_account: String::new() });
    }
}

/// The owner's switch: `mode` is "test" or "release"; `admin_account` names the administrator of release mode ("" for
/// none; it must be a registered account, so that nobody can register the name first). Refused while characters are
/// in the world: the mode decides which NPC files are loaded, so the next call rebuilds the world, which is a
/// map-server restart.
#[reducer]
pub fn set_server_mode(ctx: &ReducerContext, mode: String, admin_account: String) -> Result<(), String> {
    require_owner(ctx, "change the server mode")?;
    let new_mode = match mode.trim().to_ascii_lowercase().as_str() {
        "test" => MODE_TEST,
        "release" => MODE_RELEASE,
        _ => return Err("The mode is \"test\" or \"release\".".into()),
    };
    let admin = admin_account.trim().to_lowercase();
    if !admin.is_empty() && ctx.db.account().username_key().find(&admin).is_none() {
        return Err(format!("There is no account '{admin}': register it first, then name it."));
    }
    let old_mode = ctx.db.server_config().key().find(0).map(|r| r.mode).unwrap_or(MODE_TEST);
    if new_mode != old_mode {
        use crate::pc::pc;
        let online = ctx.db.pc().count();
        if online > 0 {
            return Err(format!("{online} character(s) are in the world: changing the mode rebuilds the world; have them log out first."));
        }
    }
    let row = ServerConfig { key: 0, mode: new_mode, admin_account: admin.clone() };
    if ctx.db.server_config().key().find(0).is_some() {
        ctx.db.server_config().key().update(row);
    } else {
        ctx.db.server_config().insert(row);
    }
    if new_mode == MODE_RELEASE {
        // the test rig's fixed roll must not outlive the test mode (it is ignored in release mode anyway: world.rs)
        use crate::world::debug_rng;
        ctx.db.debug_rng().id().delete(0);
    }
    log::info!("server mode: {} (admin account: '{}')", if new_mode == MODE_RELEASE { "release" } else { "test" }, admin);
    // the world for the new mode's NPC set, now rather than at the first player's packet
    crate::world::run(ctx, crate::npc::ensure_world);
    Ok(())
}

// ---------------------------------------------------------------------------------------------- version handshake

/// The module's side of the account / character-screen protocol (the reducers, views and their meaning). Raised when a
/// change would make an older client misbehave.
/// 2 (wave I, ionline): refusals carry rAthena's packet and code in front of their text (account.rs `refuse_login`,
/// `notify_ban`, `refuse_enter`); a protocol 1 client would show the prefix and would not ask a select again after
/// "#ban:8 Server still recognizes your last login.".
pub const PROTOCOL: u32 = 2;
/// The oldest client protocol this module still serves.
pub const MIN_CLIENT_PROTOCOL: u32 = 2;
/// The RO map protocol both sides are built for (rAthena's PACKETVER; net/packets.rs).
pub const PACKETVER: u32 = 20211103;

/// The latest Friends Package release published to GitHub Releases and registered on this SpacetimeDB server.
#[table(accessor = client_release)]
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ClientRelease {
    #[primary_key]
    pub key: u8,
    pub version: u32,
    pub commit: String,
    pub sha256: String,
    pub download_url: String,
    pub size_bytes: u64,
}

pub const PACKAGE_VERSION: u32 = 1;
pub const PACKAGE_COMMIT: &str = "";
pub const PACKAGE_SHA256: &str = "";
pub const PACKAGE_URL: &str = "";
pub const PACKAGE_BYTES: u64 = 0;

/// Updates the active Friends Package release manifest stored on SpacetimeDB (`key = 0`).
#[reducer]
pub fn set_client_release(
    ctx: &ReducerContext,
    version: u32,
    commit: String,
    sha256: String,
    download_url: String,
    size_bytes: u64,
) -> Result<(), String> {
    require_owner(ctx, "set the client release")?;
    let row = ClientRelease {
        key: 0,
        version,
        commit: commit.trim().to_string(),
        sha256: sha256.trim().to_ascii_lowercase(),
        download_url: download_url.trim().to_string(),
        size_bytes,
    };
    if ctx.db.client_release().key().find(0).is_some() {
        ctx.db.client_release().key().update(row.clone());
    } else {
        ctx.db.client_release().insert(row.clone());
    }
    log::info!(
        "client release updated: v{} ({}) {} bytes -> {}",
        row.version,
        row.commit,
        row.size_bytes,
        row.download_url
    );
    if let Some(active) = active_client_release(&ctx.db) {
        kick_all_outdated_clients(ctx, &active);
    }
    Ok(())
}

pub fn active_client_release(db: &spacetimedb::Local) -> Option<ClientRelease> {
    let row = db.client_release().key().find(0);
    let compiled = if PACKAGE_VERSION > 0 && !PACKAGE_URL.is_empty() && !PACKAGE_SHA256.is_empty() {
        Some(ClientRelease {
            key: 0,
            version: PACKAGE_VERSION,
            commit: PACKAGE_COMMIT.to_string(),
            sha256: PACKAGE_SHA256.to_string(),
            download_url: PACKAGE_URL.to_string(),
            size_bytes: PACKAGE_BYTES,
        })
    } else {
        None
    };
    match (row, compiled) {
        (Some(r), Some(c)) => {
            if c.version > r.version {
                Some(c)
            } else if r.version > 0 && !r.download_url.is_empty() && !r.sha256.is_empty() {
                Some(r)
            } else {
                Some(c)
            }
        }
        (Some(r), None) if r.version > 0 && !r.download_url.is_empty() && !r.sha256.is_empty() => Some(r),
        (_, Some(c)) => Some(c),
        _ => None,
    }
}

/// Parses `pkg:<version>` from the start of a standalone client's `hello` build string (e.g. `"pkg:3 0.1.0 WindowsPlayer"`).
/// Editor and automated smoke runs do not send `pkg:`, returning `None` so they are never redirected to the updater.
pub fn parse_pkg_version(build: &str) -> Option<u32> {
    let rest = build.strip_prefix("pkg:")?;
    let tok = rest.split_whitespace().next()?;
    tok.parse::<u32>().ok()
}

pub fn format_update_notice(rel: &ClientRelease) -> String {
    format!(
        "#update:{}:{}:{}:{}:{}",
        rel.version, rel.size_bytes, rel.sha256, rel.commit, rel.download_url
    )
}

/// Pushes `#update:...` to `my_login_notice` for `identity`, clears any character selection on its `session`,
/// and cleanly kicks + saves its character if currently in the world.
pub fn kick_outdated_identity(ctx: &ReducerContext, identity: Identity, rel: &ClientRelease) {
    use crate::account::session;
    let notice = format_update_notice(rel);
    crate::account::login_refused_for(ctx, identity, &notice);
    if let Some(mut s) = ctx.db.session().identity().find(identity) {
        if s.selected_char.is_some() {
            s.selected_char = None;
            ctx.db.session().identity().update(s);
        }
    }
    crate::world::run(ctx, |c| {
        if let Some(sd) = crate::pc::by_identity(c, identity) {
            let aid = sd.account_id;
            if crate::pc::is_active_session(c, aid) {
                crate::session::clif_authfail_fd(c, aid, 15);
            }
            crate::pc::on_client_gone(c, identity);
            if crate::pc::get(c, aid).is_some() {
                crate::pc::quit(c, aid);
            }
        }
    });
}

/// Immediately notifies and kicks all connected clients whose `client_hello` package version is older than `rel.version`.
pub fn kick_all_outdated_clients(ctx: &ReducerContext, rel: &ClientRelease) {
    let mut outdated: Vec<Identity> = Vec::new();
    for h in ctx.db.client_hello().iter() {
        if let Some(client_ver) = parse_pkg_version(&h.build) {
            if client_ver < rel.version && !outdated.contains(&h.identity) {
                outdated.push(h.identity);
            }
        }
    }
    for id in &outdated {
        kick_outdated_identity(ctx, *id, rel);
    }
    if !outdated.is_empty() {
        log::info!(
            "kicked {} connected client(s) on versions older than v{}",
            outdated.len(),
            rel.version
        );
    }
}

/// Checks whether the caller's `client_hello` reports a standalone `pkg:<version>` older than the active
/// `ClientRelease`. If so, pushes `#update:...` to `my_login_notice`, kicks any in-world character of the caller,
/// and returns `true`.
pub fn reject_if_outdated_package(ctx: &ReducerContext) -> bool {
    let Some(rel) = active_client_release(&ctx.db) else { return false };
    let row = ctx.connection_id().and_then(|c| ctx.db.client_hello().connection().find(c));
    if let Some(h) = row.as_ref() {
        if let Some(client_ver) = parse_pkg_version(&h.build) {
            if client_ver < rel.version {
                kick_outdated_identity(ctx, ctx.sender(), &rel);
                return true;
            }
        }
    }
    false
}

/// What a connected client said about itself (`hello`), until that connection closes. Kept per connection, not per
/// identity: a second connection of the same identity (two Loki windows on one PC share a saved identity) that closes
/// must not take the first one's hello with it.
#[table(accessor = client_hello)]
pub struct ClientHello {
    #[primary_key]
    pub connection: ConnectionId,
    pub identity: Identity,
    pub protocol: u32,
    pub packetver: u32,
    /// The client's build label, for the server log only.
    pub build: String,
    pub at: Timestamp,
}

/// A client names its protocol right after connecting, before `login` / `register` or a character-screen call.
/// Always accepted and recorded: the refusal, if any, comes from [`check_client`] in the calls that need it, so that an
/// outdated client reads its reason where it already shows refusals.
#[reducer]
pub fn hello(ctx: &ReducerContext, protocol: u32, packetver: u32, build: String) {
    let Some(connection) = ctx.connection_id() else { return };
    let build: String = build.chars().filter(|c| !c.is_control()).take(64).collect();
    if let (Some(client_ver), Some(rel)) = (parse_pkg_version(&build), active_client_release(&ctx.db)) {
        if client_ver < rel.version {
            kick_outdated_identity(ctx, ctx.sender(), &rel);
        }
    }
    let row = ClientHello { connection, identity: ctx.sender(), protocol, packetver, build, at: ctx.timestamp };
    if ctx.db.client_hello().connection().find(connection).is_some() {
        ctx.db.client_hello().connection().update(row);
    } else {
        ctx.db.client_hello().insert(row);
    }
}

/// What an outdated client is refused with: rAthena's login refusal 5 ("Your Game's EXE file is not the latest
/// version": login.cpp:377-407 `client_hash_check`, the nearest rule it has), with Loki's own words for it
/// (account.rs `refuse_login`).
pub const TOO_OLD: &str = "#login:5 This Loki build is too old for this server: update Loki.";

/// The pure rule: may a client that said `hello` (protocol, packetver), or never did (None), go on?
/// A hello that does not fit is refused in every mode; a client that never said hello only on a release server (the
/// test rigs' scripted client and the builds before the handshake do not say it).
pub fn client_accepted(release: bool, hello: Option<(u32, u32)>) -> bool {
    match hello {
        Some((protocol, packetver)) => protocol >= MIN_CLIENT_PROTOCOL && packetver == PACKETVER,
        None => !release,
    }
}

/// [`client_accepted`] for the caller, with the refusal text.
pub fn check_client(ctx: &ReducerContext) -> Result<(), String> {
    crate::cutover::refuse_if_frozen(ctx)?; // closed for an export / import (cutover.rs): nobody comes in
    let release = load(&ctx.db);
    let row = ctx.connection_id().and_then(|c| ctx.db.client_hello().connection().find(c));
    if let (Some(h), Some(rel)) = (row.as_ref(), active_client_release(&ctx.db)) {
        if let Some(client_ver) = parse_pkg_version(&h.build) {
            if client_ver < rel.version {
                return Err(format_update_notice(&rel));
            }
        }
    }
    let hello = row.map(|h| (h.protocol, h.packetver));
    if client_accepted(release, hello) { Ok(()) } else { Err(TOO_OLD.into()) }
}

pub fn on_disconnect(ctx: &ReducerContext) {
    if let Some(connection) = ctx.connection_id() {
        ctx.db.client_hello().connection().delete(connection);
    }
}

/// What every client may know about the server before it logs in.
#[derive(SpacetimeType)]
pub struct ServerInfo {
    /// [`PROTOCOL`]: a client that needs a newer one says "this server is older than this Loki build".
    pub protocol: u32,
    /// [`MIN_CLIENT_PROTOCOL`].
    pub min_client_protocol: u32,
    /// [`PACKETVER`].
    pub packetver: u32,
    /// [`MODE_TEST`] / [`MODE_RELEASE`].
    pub mode: u8,
}

#[view(accessor = server_info, public)]
pub fn server_info(ctx: &AnonymousViewContext) -> Option<ServerInfo> {
    let mode = ctx.db.server_config().key().find(0).map(|r| r.mode).unwrap_or(MODE_TEST);
    Some(ServerInfo { protocol: PROTOCOL, min_client_protocol: MIN_CLIENT_PROTOCOL, packetver: PACKETVER, mode })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::rules;

    /// Test mode (the default): Loki's overlay, group 0 is level 99 with every command, @lokirnd included.
    #[test]
    fn test_mode_is_the_overlay() {
        set_for_test(false);
        assert_eq!(rules::group_level(0), 99);
        assert!(rules::can_use_command(0, "item", false));
        assert!(rules::can_use_command(0, "lokirnd", false));
        assert!(rules::can_use_command(0, "item", true)); // #item on another player
        assert!(crate::data::atcommands().info("lokirnd").is_some());
        assert!(!skips_npc_file("npc/custom/loki_smoke.txt"));
        assert_eq!(world_key(), 0);
    }

    /// Release mode: rAthena's stock conf/groups.yml. A player (group 0) is level 0 with @changedress and @resurrect
    /// (conf/groups.yml "Player": Commands changedress, resurrect; Permissions can_trade, can_party, attendance); the
    /// Admin group (99) has every command but Loki's @lokirnd, which is no command there.
    #[test]
    fn release_mode_is_stock_rathena() {
        set_for_test(true);
        assert_eq!(rules::group_level(0), 0);
        assert!(rules::can_use_command(0, "changedress", false));
        assert!(rules::can_use_command(0, "resurrect", false));
        for cmd in ["item", "zeny", "warp", "baselevelup", "kick", "lokirnd", "monster", "storage", "go"] {
            assert!(!rules::can_use_command(0, cmd, false), "@{cmd} for a player");
            assert!(!rules::can_use_command(0, cmd, true), "#{cmd} for a player");
        }
        assert!(rules::has_permission(0, rules::PC_PERM_TRADE) && rules::has_permission(0, rules::PC_PERM_PARTY));
        assert!(rules::has_permission(0, rules::PC_PERM_ATTENDANCE));
        for perm in [rules::PC_PERM_USE_ALL_COMMANDS, rules::PC_PERM_WARP_ANYWHERE, rules::PC_PERM_ENABLE_COMMAND, rules::PC_PERM_CHANNEL_ADMIN, rules::PC_PERM_TRADE_UNCONDITIONAL, rules::PC_PERM_VIEW_EQUIPMENT] {
            assert!(!rules::has_permission(0, perm), "permission {perm} for a player");
        }
        assert_eq!(rules::group_level(99), 99);
        assert!(rules::can_use_command(99, "item", false) && rules::can_use_command(99, "kick", true));
        assert!(!rules::can_use_command(99, "lokirnd", false));
        assert!(crate::data::atcommands().info("lokirnd").is_none());
        assert!(crate::data::atcommands().info("item").is_some());
        assert!(skips_npc_file("npc/custom/loki_smoke.txt") && skips_npc_file("npc/custom/ggm_tests.txt"));
        assert!(!skips_npc_file("npc/re/cities/prontera.txt"));
        assert_ne!(world_key(), 0);
        set_for_test(false);
    }

    /// The stock groups are rAthena's eight (conf/groups.yml), levels as written there.
    #[test]
    fn stock_groups_are_rathenas() {
        let g: Vec<(u32, &str, u32)> = crate::data::groups_stock().iter().map(|g| (g.id, g.name.as_str(), g.level)).collect();
        assert_eq!(g, vec![(0, "Player", 0), (1, "Super Player", 0), (2, "Support", 1), (3, "Script Manager", 1), (4, "Event Manager", 1), (5, "VIP", 0), (10, "Law Enforcement", 2), (99, "Admin", 99)]);
    }

    #[test]
    fn handshake_rule() {
        // test mode: a client that never said hello plays (rocli, builds before the handshake); a wrong hello does not
        assert!(client_accepted(false, None));
        assert!(client_accepted(false, Some((PROTOCOL, PACKETVER))));
        assert!(!client_accepted(false, Some((MIN_CLIENT_PROTOCOL - 1, PACKETVER))));
        assert!(!client_accepted(false, Some((PROTOCOL, 20200101))));
        // release mode: no hello, no play
        assert!(!client_accepted(true, None));
        assert!(client_accepted(true, Some((PROTOCOL, PACKETVER))));
        assert!(client_accepted(true, Some((PROTOCOL + 5, PACKETVER)))); // a newer client decides for itself (server_info)
        assert!(!client_accepted(true, Some((0, PACKETVER))));
    }

    #[test]
    fn package_version_handshake_parse() {
        assert_eq!(parse_pkg_version("pkg:0 0.1.0 WindowsPlayer"), Some(0));
        assert_eq!(parse_pkg_version("pkg:12 0.1.0 WindowsPlayer"), Some(12));
        assert_eq!(parse_pkg_version("0.1.0 WindowsEditor"), None);
        let rel = ClientRelease {
            key: 0,
            version: 5,
            commit: "a1b2c3d4".into(),
            sha256: "deadbeef".into(),
            download_url: "https://github.com/YOUR_GITHUB_USER/loki-vr/releases/download/latest/Loki-VR.zip".into(),
            size_bytes: 123456,
        };
        assert_eq!(
            format_update_notice(&rel),
            "#update:5:123456:deadbeef:a1b2c3d4:https://github.com/YOUR_GITHUB_USER/loki-vr/releases/download/latest/Loki-VR.zip"
        );
    }
}
