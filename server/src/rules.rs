//! Configuration values rAthena reads from its conf files, fixed here as constants (defaults of rAthena e985006 for
//! PACKETVER 20211103, renewal). Each one names the file and key it comes from.

/// conf/login_athena.conf `acc_name_min_length` / `password_min_length`: 6 for PACKETVER >= 20181114 (login.cpp:760-764).
pub const ACCOUNT_NAME_MIN: usize = 6;
pub const PASSWORD_MIN: usize = 6;
/// NAME_LENGTH (23 + 1) and PASSWD_LENGTH (32 + 1) in common/mmo.hpp.
pub const NAME_MAX: usize = 23;
pub const PASSWORD_MAX: usize = 32;

/// First ids rAthena hands out (common/mmo.hpp START_ACCOUNT_NUM / START_CHAR_NUM); kept so ids look like the ones
/// Loki already sees from rAthena.
pub const START_ACCOUNT_ID: u32 = 2_000_000;
pub const START_CHAR_ID: u32 = 150_000;

/// MAX_CHARS for PACKETVER >= 20180124 (common/mmo.hpp:68) with no VIP / billing slots: 15 slots per account.
pub const CHAR_SLOTS: u8 = 15;

/// conf/char_athena.conf `char_name_min_length: 4`, `char_name_option: 1` with `char_name_letters` (only these may be used).
pub const CHAR_NAME_MIN: usize = 4;
pub const CHAR_NAME_LETTERS: &str = "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
/// char/char.hpp TRIM_CHARS "\255\xA0\032\t\x0A\x0D ": trimmed from both ends and collapsed to one space inside
/// (strlib.cpp normalize_name). `\255` and `\032` are octal escapes: 0xAD and 0x1A.
pub const TRIM_CHARS: &[char] = &['\u{ad}', '\u{a0}', '\u{1a}', '\t', '\n', '\r', ' '];
/// conf/char_athena.conf `wisp_server_name: Server` — reserved (char_check_char_name).
pub const WISP_SERVER_NAME: &str = "Server";

/// conf/char_athena.conf `start_status_points` default 48 (char.cpp:2811) — the stat points a new character gets
/// (PACKETVER >= 20120307: the client no longer picks stats; all six start at 1).
pub const START_STATUS_POINTS: u32 = 48;
/// conf/char_athena.conf `start_zeny: 0`.
pub const START_ZENY: u32 = 0;

/// JOB_NOVICE / JOB_SUMMONER (map/pc.hpp e_job) — the two classes a new character may start as (`allowed_job_flag: 3`).
pub const JOB_NOVICE: u16 = 0;
pub const JOB_SUMMONER: u16 = 4218;

/// conf/char_athena.conf `start_point` (renewal): one of these, chosen at random (char.cpp:1400).
pub const START_POINTS: &[(&str, u16, u16)] =
    &[("iz_int", 18, 26), ("iz_int01", 18, 26), ("iz_int02", 18, 26), ("iz_int03", 18, 26), ("iz_int04", 18, 26)];
/// conf/char_athena.conf `start_point_doram`.
pub const START_POINTS_DORAM: &[(&str, u16, u16)] = &[("lasa_fild01", 48, 297)];

/// conf/char_athena.conf `start_items` (renewal) and `start_items_doram`: item id, amount, equip location bits.
pub const START_ITEMS: &[(u32, u16, u32)] = &[(1201, 1, 2), (2301, 1, 16), (23484, 1, 0)];
pub const START_ITEMS_DORAM: &[(u32, u16, u32)] = &[(1681, 1, 2), (2301, 1, 16)];

/// conf/char_athena.conf `char_del_delay: 86400` (seconds before a reserved deletion can be confirmed),
/// `char_del_level: 0` (no level limit), `char_del_restriction: 3` (not while in a party or a guild).
pub const CHAR_DEL_DELAY_SECS: i64 = 86_400;

/// conf/login_athena.conf:131-140 `ipban_enable: yes`, `ipban_dynamic_pass_failure_ban: yes`: a source that fails
/// `ipban_dynamic_pass_failure_ban_limit: 7` logins (unknown account or wrong password) within
/// `ipban_dynamic_pass_failure_ban_interval: 5` minutes is refused for `ipban_dynamic_pass_failure_ban_duration: 5`
/// minutes (login/loginclif.cpp:222-223, login/ipban.cpp:71-88; login.cpp:770-774 the defaults). rAthena's source is
/// the IP address; a reducer has none, so here it is the caller's identity (account.rs `login_source`).
pub const LOGIN_FAIL_LIMIT: u32 = 7;
pub const LOGIN_FAIL_WINDOW_SECS: i64 = 5 * 60;
pub const LOGIN_LOCK_SECS: i64 = 5 * 60;

/// conf/login_athena.conf:70-71 `allowed_regs: 1` registration in `time_allowed: 10` seconds, for the whole server
/// (login.cpp login_mmo_auth_new:225-236, 273-277). Enforced in release mode only (release.rs): the test rigs register
/// their accounts in a burst.
pub const ALLOWED_REGS: u32 = 1;
pub const TIME_ALLOWED_SECS: i64 = 10;

/// conf/char_athena.conf:247-264 `char_move_enabled: yes`, `char_movetoused: yes`, `char_moves_unlimited: no`,
/// `char_rename_party: no`, `char_rename_guild: no` (char.cpp:2741-2743 the defaults).
pub const CHAR_MOVE_ENABLED: bool = true;
pub const CHAR_MOVETOUSED: bool = true;
pub const CHAR_MOVES_UNLIMITED: bool = false;
pub const CHAR_RENAME_PARTY: bool = false;
pub const CHAR_RENAME_GUILD: bool = false;

/// common/socket.cpp:255-257 RFIFO_SIZE: a client connection's receive buffer ("this will also be the max. size"). A
/// client that fills it is disconnected (socket.cpp:1031-1035), so no client packet, and no burst of them read in one
/// pass, reaches 2048 bytes (the largest known, the guild emblem 0x0153, is under 1,800).
pub const RFIFO_SIZE: usize = 2 * 1024;

// ---------------------------------------------------------------------------------------------- player groups

/// pc_groups.hpp e_pc_permission.
pub const PC_PERM_TRADE: u32 = 0;
pub const PC_PERM_PARTY: u32 = 1;
pub const PC_PERM_ALL_SKILL: u32 = 2;
pub const PC_PERM_USE_ALL_EQUIPMENT: u32 = 3;
pub const PC_PERM_SKILL_UNCONDITIONAL: u32 = 4;
pub const PC_PERM_JOIN_ALL_CHAT: u32 = 5;
pub const PC_PERM_NO_CHAT_KICK: u32 = 6;
pub const PC_PERM_HIDE_SESSION: u32 = 7;
pub const PC_PERM_WHO_DISPLAY_AID: u32 = 8;
pub const PC_PERM_RECEIVE_HACK_INFO: u32 = 9;
pub const PC_PERM_WARP_ANYWHERE: u32 = 10;
pub const PC_PERM_VIEW_HPMETER: u32 = 11;
pub const PC_PERM_VIEW_EQUIPMENT: u32 = 12;
pub const PC_PERM_USE_CHECK: u32 = 13;
pub const PC_PERM_USE_CHANGEMAPTYPE: u32 = 14;
pub const PC_PERM_USE_ALL_COMMANDS: u32 = 15;
pub const PC_PERM_RECEIVE_REQUESTS: u32 = 16;
pub const PC_PERM_SHOW_BOSS: u32 = 17;
pub const PC_PERM_DISABLE_PVM: u32 = 18;
pub const PC_PERM_DISABLE_PVP: u32 = 19;
pub const PC_PERM_DISABLE_CMD_DEAD: u32 = 20;
pub const PC_PERM_CHANNEL_ADMIN: u32 = 21;
pub const PC_PERM_TRADE_BOUNDED: u32 = 22;
pub const PC_PERM_ITEM_UNCONDITIONAL: u32 = 23;
pub const PC_PERM_ENABLE_COMMAND: u32 = 24;
pub const PC_PERM_BYPASS_STAT_ONCLONE: u32 = 25;
pub const PC_PERM_BYPASS_MAX_STAT: u32 = 26;
pub const PC_PERM_ATTENDANCE: u32 = 27;
pub const PC_PERM_MACRO_DETECT: u32 = 28;
pub const PC_PERM_MACRO_REGISTER: u32 = 29;
pub const PC_PERM_TRADE_UNCONDITIONAL: u32 = 30;

/// The player group database in force: Loki's test overlay (conf/import/groups.yml: group 0 is level 99 with every
/// command) or, in release mode, rAthena's stock conf/groups.yml (release.rs).
pub fn groups() -> &'static Vec<crate::data::PlayerGroup> {
    if crate::release::is_release() { crate::data::groups_stock() } else { crate::data::groups() }
}

pub fn group(group_id: u8) -> Option<&'static crate::data::PlayerGroup> {
    groups().iter().find(|g| g.id == group_id as u32)
}

/// pc_get_group_level.
pub fn group_level(group_id: u8) -> i32 {
    group(group_id).map(|g| g.level as i32).unwrap_or(0)
}

/// pc_has_permission.
pub fn has_permission(group_id: u8, perm: u32) -> bool {
    group(group_id).map(|g| g.permissions & (1 << perm) != 0).unwrap_or(false)
}

/// s_player_group::can_use_command (`command` without the symbol).
pub fn can_use_command(group_id: u8, command: &str, charcommand: bool) -> bool {
    let Some(g) = group(group_id) else { return false };
    if crate::release::hides_command(command) {
        return false; // a test-rig command does not exist on a release server (release.rs)
    }
    if g.permissions & (1 << PC_PERM_USE_ALL_COMMANDS) != 0 {
        return true;
    }
    let lower = command.to_ascii_lowercase();
    if charcommand { g.char_commands.contains(&lower) } else { g.commands.contains(&lower) }
}
