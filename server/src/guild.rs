//! map/guild.cpp: guilds on the map server — its guild_db (`guild_db`, a copy of the char server's guild kept up to
//! date from int_guild.rs's answers), creation, invitations, leaving / expelling, positions, notice, emblems, member
//! logins and minimap dots (guild_send_xy_timer), guild chat, guild EXP (guild_payexp and its cache flushed by
//! guild_payexp_timer), guild skills, alliances and oppositions, breaking a guild and changing its master. The
//! packets are in clif_guild.rs, the guild storage in guild_storage.rs.
//!
//! map_session_data's guild fields are the player's `pc_guild` row: sd->guild (the MapGuild the player is attached
//! to, as its guild id; 0 = nullptr), state.gmaster_flag, guild_invite(_account), guild_alliance(_account),
//! guild_x / guild_y and guildspy. sd->status.guild_id is the character's `guild_id` (mirrored in the unit row, which
//! the unit packets read as status_get_guild_id) and sd->guild_emblem_id is the unit's `emblem_id`
//! (status_get_emblem_id). A guild member's `sd` is the account id of its online player (0 = none).
//!
//! The War of Emperium (castles, the agit flags' changes, guardians, guild flags, castle packets) is woe.rs's: the agit
//! flags below are the state the rest of the code reads (woe.rs sets them); guild_checkcastles, castle_guild_broken_sub
//! and guild_emblem_changed's guardian / flag part call into woe.rs.

use spacetimedb::{table, Table};

use crate::character::character;
use crate::clif;
use crate::int_guild::*;
use crate::intif::{self, Inter};
use crate::pc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::Ctx;

// ------------------------------------------------------------------------------------------------ WoE flags

#[table(accessor = agit_state)]
pub struct AgitState {
    #[primary_key]
    pub key: u8,
    pub agit: bool,
    pub agit2: bool,
    pub agit3: bool,
}

fn agit_state(c: &Ctx) -> AgitState {
    c.db().agit_state().key().find(0).unwrap_or(AgitState { key: 0, agit: false, agit2: false, agit3: false })
}

/// map.cpp agit_flag (WoE FE), agit2_flag (SE), agit3_flag (TE): woe.rs guild_agit_start / end (agitstart…, @agitstart…).
pub fn agit_flag(c: &Ctx) -> bool {
    agit_state(c).agit
}
pub fn agit2_flag(c: &Ctx) -> bool {
    agit_state(c).agit2
}
pub fn agit3_flag(c: &Ctx) -> bool {
    agit_state(c).agit3
}

/// map.hpp is_agit_start().
pub fn is_agit_start(c: &Ctx) -> bool {
    let s = agit_state(c);
    s.agit || s.agit2 || s.agit3
}

// ------------------------------------------------------------------------------------------------ config

/// guild.cpp GUILD_SEND_XY_INTERVAL / GUILD_PAYEXP_INTERVAL.
pub const GUILD_SEND_XY_INTERVAL: i64 = 5000;
pub const GUILD_PAYEXP_INTERVAL: i64 = 10000;
/// conf/battle/guild.conf `guild_emperium_check: yes`.
pub fn guild_emperium_check() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::GUILD_EMPERIUM_CHECK) != 0
}
/// conf/battle/guild.conf `guild_exp_limit: 50`.
pub fn guild_exp_limit() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::GUILD_EXP_LIMIT)
}
/// conf/battle/guild.conf `max_guild_alliance: 3`.
pub fn max_guild_alliance() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAX_GUILD_ALLIANCE)
}
/// conf/battle/guild.conf `guild_alliance_onlygm: no`.
pub fn guild_alliance_onlygm() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::GUILD_ALLIANCE_ONLYGM) != 0
}
/// conf/battle/guild.conf `guild_notice_changemap: 2`.
pub fn guild_notice_changemap() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::GUILD_NOTICE_CHANGEMAP)
}
/// conf/battle/guild.conf `guild_leaderchange_delay: 1440` (compared with seconds: DIFF_TICK(time(nullptr), …)).
pub fn guild_leaderchange_delay() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::GUILD_LEADERCHANGE_DELAY) as i64
}
/// conf/battle/guild.conf `guild_leaderchange_woe: no`.
pub fn guild_leaderchange_woe() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::GUILD_LEADERCHANGE_WOE) != 0
}
/// conf/battle/guild.conf `require_glory_guild: no`.
pub fn require_glory_guild() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::REQUIRE_GLORY_GUILD) != 0
}
/// conf/battle/player.conf `invite_request_check: yes`.
pub fn invite_request_check() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INVITE_REQUEST_CHECK) != 0
}
/// conf/battle/instance.conf instance_block_*: yes.
pub fn instance_block_leave() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INSTANCE_BLOCK_LEAVE) != 0
}
pub fn instance_block_leaderchange() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INSTANCE_BLOCK_LEADERCHANGE) != 0
}
pub fn instance_block_invite() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INSTANCE_BLOCK_INVITE) != 0
}
pub fn instance_block_expulsion() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::INSTANCE_BLOCK_EXPULSION) != 0
}
/// conf/map_athena.conf `enable_spy: no` (@guildspy / @partyspy are refused, GUILD targets skip the spies).
pub const ENABLE_SPY: bool = false;
/// conf/inter_athena.conf `emblem_woe_change: yes`, `emblem_transparency_limit: 80`.
pub const EMBLEM_WOE_CHANGE: bool = true;
pub const EMBLEM_TRANSPARENCY_LIMIT: i32 = 80;
/// item_db ITEMID_EMPERIUM.
pub const ITEMID_EMPERIUM: u32 = 714;
/// map.hpp MF_GUILDLOCK (script_constants: 45).
pub const MF_GUILDLOCK: i16 = 45;
/// mmo.hpp GD_GLORYGUILD.
const GD_GLORYGUILD: u16 = 10005;
/// status.hpp SC_LEADERSHIP, SC_GLORYWOUNDS, SC_SOULCOLD, SC_HAWKEYES, SC_EMERGENCY_MOVE.
const GUILD_SCS: [i32; 5] = [
    crate::status::consts::SC_LEADERSHIP,
    crate::status::consts::SC_GLORYWOUNDS,
    crate::status::consts::SC_SOULCOLD,
    crate::status::consts::SC_HAWKEYES,
    crate::status::consts::SC_EMERGENCY_MOVE,
];

// ------------------------------------------------------------------------------------------------ tables

/// guild.hpp class MapGuild: the map server's guild_db. `channel` (the #ally channel) is the channel row
/// `channel::ally_key(guild_id)` when it exists.
#[table(accessor = guild_db)]
#[derive(Clone, Debug)]
pub struct MapGuild {
    #[primary_key]
    pub guild_id: u32,
    pub guild: MmoGuild,
    pub instance_id: i32,
    pub chargeshout_flag_id: i32,
}

/// map_session_data's guild fields (see the module doc).
#[table(accessor = pc_guild)]
#[derive(Clone, Debug)]
pub struct PcGuild {
    #[primary_key]
    pub account_id: u32,
    /// sd->guild: the guild id of the MapGuild the player points at (0 = nullptr).
    pub guild: u32,
    pub gmaster_flag: bool,
    pub guild_invite: i32,
    pub guild_invite_account: u32,
    pub guild_alliance: i32,
    pub guild_alliance_account: u32,
    pub guild_x: i16,
    pub guild_y: i16,
    pub guildspy: i32,
}

/// guild.cpp struct guild_expcache (guild_expcache_db, keyed by char id).
#[table(accessor = guild_expcache)]
#[derive(Clone, Debug)]
pub struct GuildExpCache {
    #[primary_key]
    pub char_id: u32,
    pub guild_id: i32,
    pub account_id: u32,
    pub exp: u64,
}

/// guild.cpp guild_infoevent_db: the NPC events waiting for a guild's info (guild_npc_request_info). The newest event
/// heads each guild's list (`seq` grows; the list runs from the highest seq down).
#[table(accessor = guild_infoevent, index(accessor = by_guild, btree(columns = [guild_id])))]
#[derive(Clone, Debug)]
pub struct GuildInfoEvent {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub guild_id: i32,
    pub seq: u64,
    pub name: String,
}

/// guild.cpp's timers (do_init_guild: guild_payexp_timer and guild_send_xy_timer, both intervals).
#[table(accessor = guild_state)]
pub struct GuildState {
    #[primary_key]
    pub key: u8,
    pub payexp_timer: u64,
    pub xy_timer: u64,
    pub infoevent_seq: u64,
}

fn gs(c: &Ctx) -> GuildState {
    c.db().guild_state().key().find(0).unwrap_or(GuildState { key: 0, payexp_timer: 0, xy_timer: 0, infoevent_seq: 0 })
}

fn put_gs(c: &Ctx, s: GuildState) {
    if c.db().guild_state().key().find(0).is_some() {
        c.db().guild_state().key().update(s);
    } else {
        c.db().guild_state().insert(s);
    }
}

/// do_init_guild: add_timer_interval(gettick() + GUILD_PAYEXP_INTERVAL, guild_payexp_timer, …) and
/// add_timer_interval(gettick() + GUILD_SEND_XY_INTERVAL, guild_send_xy_timer, …). The module arms them the first time
/// the map server learns about a guild (and re-arms one if its row is ever gone).
pub fn ensure_timers(c: &Ctx) {
    let mut s = gs(c);
    let mut changed = false;
    if s.payexp_timer == 0 || timer::get(c, s.payexp_timer).is_none() {
        s.payexp_timer = timer::add_interval(c, c.tick() + GUILD_PAYEXP_INTERVAL, Func::GuildPayexpTimer, 0, 0, GUILD_PAYEXP_INTERVAL);
        changed = true;
    }
    if s.xy_timer == 0 || timer::get(c, s.xy_timer).is_none() {
        s.xy_timer = timer::add_interval(c, c.tick() + GUILD_SEND_XY_INTERVAL, Func::GuildSendXyTimer, 0, 0, GUILD_SEND_XY_INTERVAL);
        changed = true;
    }
    if changed {
        put_gs(c, s);
    }
}

// ------------------------------------------------------------------------------------------------ player state

/// The player's guild fields (pc_setnewpc: guild_x = guild_y = -1, the rest zero).
pub fn gx(c: &Ctx, aid: u32) -> PcGuild {
    c.db().pc_guild().account_id().find(aid).unwrap_or(PcGuild {
        account_id: aid,
        guild: 0,
        gmaster_flag: false,
        guild_invite: 0,
        guild_invite_account: 0,
        guild_alliance: 0,
        guild_alliance_account: 0,
        guild_x: -1,
        guild_y: -1,
        guildspy: 0,
    })
}

pub fn put_gx(c: &Ctx, x: &PcGuild) {
    if c.db().pc_guild().account_id().find(x.account_id).is_some() {
        c.db().pc_guild().account_id().update(x.clone());
    } else {
        c.db().pc_guild().insert(x.clone());
    }
}

/// unit_free: the player's guild fields go with the session.
pub fn drop_gx(c: &Ctx, aid: u32) {
    c.db().pc_guild().account_id().delete(aid);
}

/// sd->status.guild_id.
pub fn guild_id_of(c: &Ctx, aid: u32) -> i32 {
    pc::character_of(c, aid).map(|ch| ch.guild_id as i32).unwrap_or(0)
}

/// sd->status.guild_id = guild_id (the unit's copy follows: status_get_guild_id).
pub fn set_guild_id(c: &Ctx, aid: u32, guild_id: i32) {
    let gid = guild_id.max(0) as u32;
    if let Some(mut ch) = pc::character_of(c, aid) {
        if ch.guild_id != gid {
            ch.guild_id = gid;
            c.db().character().id().update(ch);
        }
    }
    if let Some(mut u) = unit::get(c, aid) {
        if u.guild_id != gid {
            u.guild_id = gid;
            unit::save(c, &u);
        }
    }
}

/// sd->guild_emblem_id (the unit's emblem_id: status_get_emblem_id).
pub fn emblem_id_of(c: &Ctx, aid: u32) -> i32 {
    unit::get(c, aid).map(|u| u.emblem_id).unwrap_or(0)
}

pub fn set_emblem_id(c: &Ctx, aid: u32, emblem_id: i32) {
    if let Some(mut u) = unit::get(c, aid) {
        if u.emblem_id != emblem_id {
            u.emblem_id = emblem_id;
            unit::save(c, &u);
        }
    }
}

/// sd->guild = g (guild id, 0 = nullptr).
fn set_sd_guild(c: &Ctx, aid: u32, guild_id: u32) {
    let mut x = gx(c, aid);
    if x.guild != guild_id {
        x.guild = guild_id;
        put_gx(c, &x);
    }
}

/// sd->guild: the guild the player is attached to.
pub fn sd_guild(c: &Ctx, aid: u32) -> Option<MapGuild> {
    let g = gx(c, aid).guild;
    if g == 0 {
        return None;
    }
    guild_search(c, g as i32)
}

fn name_of(c: &Ctx, aid: u32) -> String {
    pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default()
}

/// map_id2sd(account_id) is a player (its account id).
fn id2sd(c: &Ctx, aid: u32) -> Option<u32> {
    if aid == 0 {
        return None;
    }
    pc::get(c, aid).map(|_| aid)
}

/// session_isActive(sd->fd) (a player waiting out clif_delayquit has no fd).
pub fn session_active(c: &Ctx, aid: u32) -> bool {
    pc::get(c, aid).map(|p| !p.disconnected).unwrap_or(false)
}

/// `sd->clan != nullptr` (guild_create, guild_invite, @guild, CreateGuild): the extras agent's session clan.
pub fn in_clan(c: &Ctx, aid: u32) -> bool {
    crate::clan::sd_clan(c, aid).is_some()
}

/// status_change_end(sd, type) for the guild auras / Emergency Move.
fn end_guild_scs(c: &Ctx, aid: u32) {
    for sc in GUILD_SCS {
        crate::status::sc::status_change_end(c, aid, sc, 0);
    }
}

// ------------------------------------------------------------------------------------------------ lookups

/// guild.cpp guild_search.
pub fn guild_search(c: &Ctx, guild_id: i32) -> Option<MapGuild> {
    if guild_id <= 0 {
        return None;
    }
    c.db().guild_db().guild_id().find(guild_id as u32)
}

pub fn put_guild(c: &Ctx, g: &MapGuild) {
    if c.db().guild_db().guild_id().find(g.guild_id).is_some() {
        c.db().guild_db().guild_id().update(g.clone());
    } else {
        c.db().guild_db().insert(g.clone());
    }
}

/// guild_db in the order the module walks it (rAthena's unordered_map order is not observable by any client: each
/// guild's packets go to that guild's members).
pub fn guild_db_iter(c: &Ctx) -> Vec<MapGuild> {
    let mut v: Vec<MapGuild> = c.db().guild_db().iter().collect();
    v.sort_by_key(|g| g.guild_id);
    v
}

/// guild.cpp guild_searchname (strcmp).
pub fn guild_searchname(c: &Ctx, name: &str) -> Option<MapGuild> {
    guild_db_iter(c).into_iter().find(|g| g.guild.name == name)
}

/// guild.cpp guild_searchnameid: by name first, then by atoi(str) as an id.
pub fn guild_searchnameid(c: &Ctx, s: &str) -> Option<MapGuild> {
    if let Some(g) = guild_searchname(c, s) {
        return Some(g);
    }
    guild_search(c, crate::cstr::atoi(s) as i32)
}

/// guild.cpp guild_getavailablesd: the first online member (account id).
pub fn guild_getavailablesd(g: &MmoGuild) -> Option<u32> {
    (0..g.max()).map(|i| g.member[i].sd).find(|&sd| sd != 0)
}

/// guild.cpp guild_getindex.
pub fn guild_getindex(g: &MmoGuild, account_id: u32, char_id: u32) -> Option<usize> {
    (0..g.max()).find(|&i| g.member[i].account_id == account_id && g.member[i].char_id == char_id)
}

/// guild.cpp guild_getposition: the player's position index, -1 without a guild.
pub fn guild_getposition(c: &Ctx, aid: u32) -> i32 {
    let Some(g) = sd_guild(c, aid) else { return -1 };
    let cid = pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    match guild_getindex(&g.guild, aid, cid) {
        Some(i) => g.guild.member[i].position as i32,
        None => -1,
    }
}

/// guild.cpp guild_skill_get_index (SKILL_CHK_GUILD range, then the MAX_GUILDSKILL bound).
fn guild_skill_get_index(skill_id: i32) -> i32 {
    if !(0..=u16::MAX as i32).contains(&skill_id) {
        return -1;
    }
    crate::db::skills::guild_skill_get_index(skill_id as u16) as i32
}

/// guild.cpp guild_checkskill: the level the guild learned.
pub fn guild_checkskill(g: &MmoGuild, id: i32) -> i32 {
    let idx = guild_skill_get_index(id);
    if idx < 0 {
        return 0;
    }
    g.skill[idx as usize].lv
}

/// guild.cpp guild_check_skill_require.
pub fn guild_check_skill_require(g: &MmoGuild, id: i32) -> bool {
    crate::db::skills::guild_check_skill_require(id as u16, |sid| guild_checkskill(g, sid as i32))
}

/// guild.cpp guild_has_permission.
pub fn guild_has_permission(c: &Ctx, aid: u32, permission: i32) -> bool {
    let position = guild_getposition(c, aid);
    if position < 0 {
        return false;
    }
    let Some(g) = sd_guild(c, aid) else { return false };
    g.guild.position.get(position as usize).map(|p| p.mode & permission != 0).unwrap_or(false)
}

/// guild.cpp guild_checkcastles: castles the guild owns (woe.rs).
pub fn guild_checkcastles(c: &Ctx, g: &MmoGuild) -> i32 {
    crate::woe::guild_checkcastles(c, g.guild_id)
}

/// guild.cpp guild_isallied.
pub fn guild_isallied(c: &Ctx, guild_id: i32, guild_id2: i32) -> bool {
    let Some(g) = guild_search(c, guild_id) else { return false };
    match (0..MAX_GUILDALLIANCE).find(|&i| g.guild.alliance[i].guild_id == guild_id2) {
        Some(i) => g.guild.alliance[i].opposition == 0,
        None => false,
    }
}

/// guild.cpp guild_check_alliance (flag 0 allied, 1 enemy).
pub fn guild_check_alliance(c: &Ctx, guild_id1: i32, guild_id2: i32, flag: i32) -> bool {
    let Some(g) = guild_search(c, guild_id1) else { return false };
    (0..MAX_GUILDALLIANCE).any(|i| g.guild.alliance[i].guild_id == guild_id2 && g.guild.alliance[i].opposition == flag)
}

/// guild.cpp guild_get_alliance_count (flag 0 allied, 1 enemy).
pub fn guild_get_alliance_count(g: &MmoGuild, flag: i32) -> i32 {
    (0..MAX_GUILDALLIANCE).filter(|&i| g.alliance[i].guild_id > 0 && g.alliance[i].opposition == flag).count() as i32
}

/// The guild name and position name a player's name tag shows (clif_name): None when sd->guild is null. (clif_name's
/// `else if (sd->clan)` puts the clan name as the position: pc::name_parts with crate::clan::clan_name_of.)
pub fn name_parts(c: &Ctx, aid: u32) -> Option<(String, String)> {
    let g = sd_guild(c, aid)?;
    let position = guild_getposition(c, aid);
    // guild.position[position] (a player attached to a guild it is not a member of reads index -1: rAthena reads
    // out of bounds there; the module shows no position)
    let pname = if position >= 0 { g.guild.position.get(position as usize).map(|p| p.name.clone()).unwrap_or_default() } else { String::new() };
    Some((g.guild.name.clone(), pname))
}

// ------------------------------------------------------------------------------------------------ intif requests

/// intif.cpp intif_guild_leave (mes: safestrncpy 40).
pub fn intif_guild_leave(guild_id: i32, account_id: u32, char_id: u32, flag: u8, mes: &str) -> bool {
    intif::send(Inter::GuildLeave { guild_id, account_id, char_id, flag, mes: trunc(mes, 40) });
    true
}

/// intif.cpp intif_guild_change_memberinfo.
pub fn intif_guild_change_memberinfo(guild_id: i32, account_id: u32, char_id: u32, kind: u16, value: i64) {
    intif::send(Inter::GuildMemberInfoChange { guild_id, account_id, char_id, kind, value });
}

/// intif.cpp intif_guild_change_basicinfo.
pub fn intif_guild_change_basicinfo(guild_id: i32, kind: u16, value: i64) {
    intif::send(Inter::GuildBasicInfoChange { guild_id, kind, value });
}

/// intif.cpp intif_guild_alliance.
pub fn intif_guild_alliance(guild_id1: i32, guild_id2: i32, account_id1: u32, account_id2: u32, flag: i32) {
    intif::send(Inter::GuildAlliance { guild_id1, guild_id2, account_id1, account_id2, flag });
}

/// guild.cpp guild_request_info (intif_guild_request_info).
pub fn guild_request_info(guild_id: i32) {
    intif::send(Inter::GuildInfo { guild_id });
}

// ------------------------------------------------------------------------------------------------ guild.cpp

/// guild.cpp guild_sd_check: the online member's sd (account id), 0 when not online; a player that belongs to
/// another guild is kicked out of this one.
fn guild_sd_check(c: &Ctx, guild_id: i32, account_id: u32, char_id: u32) -> u32 {
    let Some(p) = pc::get(c, account_id) else { return 0 };
    if p.char_id != char_id {
        return 0;
    }
    if guild_id_of(c, account_id) != guild_id {
        // If player belongs to a different guild, kick him out.
        intif_guild_leave(guild_id, account_id, char_id, 0, "** Guild Mismatch **");
        return 0;
    }
    account_id
}

/// guild.cpp guild_makemember: the member entry for this player.
fn guild_makemember(c: &Ctx, aid: u32) -> Option<GuildMember> {
    let ch = pc::character_of(c, aid)?;
    Some(GuildMember {
        account_id: ch.account_id,
        char_id: ch.id,
        hair: ch.hair as i16,
        hair_color: ch.hair_color as i16,
        gender: ch.sex as i16,
        class: ch.class as i16,
        lv: ch.base_level as i16,
        exp: 0,
        online: 1,
        position: MAX_GUILDPOSITION as i16 - 1,
        name: trunc(&ch.name, NAME_LENGTH),
        sd: 0,
        modified: 0,
        last_login: (c.tick() / 1000) as u32, // time(nullptr)
    })
}

/// guild.cpp guild_payexp_timer (+ guild_payexp_timer_sub): the cached taxed EXP goes to the char server.
pub fn guild_payexp_timer(c: &Ctx) {
    let mut rows: Vec<GuildExpCache> = c.db().guild_expcache().iter().collect();
    rows.sort_by_key(|r| crate::party::idb_order(r.char_id));
    for r in rows {
        c.db().guild_expcache().char_id().delete(r.char_id);
        let Some(mut g) = guild_search(c, r.guild_id) else { continue };
        let Some(i) = guild_getindex(&g.guild, r.account_id, r.char_id) else { continue };
        g.guild.member[i].exp = g.guild.member[i].exp.saturating_add(r.exp).min(MAX_GUILD_EXP);
        let exp = g.guild.member[i].exp;
        put_guild(c, &g);
        intif_guild_change_memberinfo(g.guild.guild_id, r.account_id, r.char_id, GMI_EXP, exp as i64);
    }
}

/// guild.cpp guild_send_xy_timer (+ guild_send_xy_timer_sub): each online member's position when it changed.
pub fn guild_send_xy_timer(c: &Ctx) {
    for g in guild_db_iter(c) {
        if g.guild.connect_member <= 0 {
            continue; // no members connected to this guild so do not iterate
        }
        for i in 0..g.guild.max() {
            let aid = g.guild.member[i].sd;
            if aid == 0 {
                continue;
            }
            // sd->fd (a player waiting out clif_delayquit has none); !sd->bg_id (battleground members show no guild dot)
            if !session_active(c, aid) || crate::battleground::bg_id_of(c, aid) != 0 {
                continue;
            }
            let Some(sd) = unit::get(c, aid) else { continue };
            let mut x = gx(c, aid);
            if x.guild_x != sd.x || x.guild_y != sd.y {
                crate::clif_guild::clif_guild_xy(c, &sd);
                x.guild_x = sd.x;
                x.guild_y = sd.y;
                put_gx(c, &x);
            }
        }
    }
}

/// guild.cpp guild_send_dot_remove.
pub fn guild_send_dot_remove(c: &Ctx, sd: &Unit) {
    if guild_id_of(c, sd.id) != 0 {
        crate::clif_guild::clif_guild_xy_remove(c, sd);
    }
}

/// guild.cpp guild_create (`emperium_check` is battle_config.guild_emperium_check, which @guild switches off around
/// the call).
pub fn guild_create(c: &Ctx, aid: u32, name: &str, emperium_check: bool) -> bool {
    let Some(sd) = unit::get(c, aid) else { return false };
    // safestrncpy(tname, name, NAME_LENGTH); trim(tname)
    let tname = trunc(name, NAME_LENGTH);
    let tb: Vec<u8> = crate::net::wire::latin1(&tname);
    if tb.iter().all(|&b| crate::cstr::is_space(b)) {
        return false; // empty name
    }
    if guild_id_of(c, aid) != 0 {
        // already in a guild
        crate::clif_guild::clif_guild_created(c, &sd, 1);
        return false;
    }
    if emperium_check && crate::inventory::pc_search_inventory(c, aid, ITEMID_EMPERIUM) == -1 {
        // item required
        crate::clif_guild::clif_guild_created(c, &sd, 3);
        return false;
    }
    let Some(mut m) = guild_makemember(c, aid) else { return false };
    m.position = 0;
    // intif_guild_create: safestrncpy(name, NAME_LENGTH) of the untrimmed name
    intif::send(Inter::CreateGuild { account_id: aid, name: trunc(name, NAME_LENGTH), master: m });
    true
}

/// guild.cpp guild_created (the char server's answer; guild id 0 = the name exists or is not allowed).
pub fn guild_created(c: &Ctx, account_id: u32, guild_id: i32) {
    let Some(sd) = id2sd(c, account_id).and_then(|a| unit::get(c, a)) else { return };
    if guild_id == 0 {
        crate::clif_guild::clif_guild_created(c, &sd, 2); // Creation failure (presence of the same name Guild)
        return;
    }
    set_guild_id(c, account_id, guild_id);
    crate::clif_guild::clif_guild_created(c, &sd, 0);
    if guild_emperium_check() {
        let index = crate::inventory::pc_search_inventory(c, account_id, ITEMID_EMPERIUM);
        if index > 0 {
            // emperium consumption (`index > 0`: an Emperium in the first slot is not taken)
            crate::inventory::pc_delitem(c, account_id, index as i32, 1, 0, 0, crate::map_log::LOG_TYPE_CONSUME);
        }
    }
}

/// guild.cpp guild_npc_request_info: runs `event` (npc_event_doall) once the guild's info is known.
pub fn guild_npc_request_info(c: &Ctx, guild_id: i32, event: &str) -> i32 {
    if guild_search(c, guild_id).is_some() {
        if !event.is_empty() {
            crate::script::npc_run::npc_event_doall(c, event);
        }
        return 0;
    }
    if !event.is_empty() {
        let mut s = gs(c);
        s.infoevent_seq += 1;
        let seq = s.infoevent_seq;
        put_gs(c, s);
        // safestrncpy(ev->name, event, EVENT_NAME_LENGTH); the one in the db (if present) becomes its next
        c.db().guild_infoevent().insert(GuildInfoEvent { id: 0, guild_id, seq, name: trunc(event, 77) });
    }
    guild_request_info(guild_id);
    0
}

/// guild_npc_request_info's branch for a guild that is not loaded: `event` waits for the guild's info (the newest
/// waiting event of a guild runs first) and the info is requested. woe.rs's guild_castledataloadack takes it for the
/// last owned castle at a world build, where rAthena's guild_db is always empty (a map-server start).
pub fn guild_npc_queue_info_event(c: &Ctx, guild_id: i32, event: &str) -> i32 {
    if !event.is_empty() {
        let mut s = gs(c);
        s.infoevent_seq += 1;
        let seq = s.infoevent_seq;
        put_gs(c, s);
        c.db().guild_infoevent().insert(GuildInfoEvent { id: 0, guild_id, seq, name: trunc(event, 77) });
    }
    guild_request_info(guild_id);
    0
}

/// mapit_getallusers: every player in the world (account ids).
fn all_users(c: &Ctx) -> Vec<u32> {
    use crate::pc::pc as _;
    let mut v: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    v.sort_by_key(|&a| crate::party::idb_order(a));
    v
}

/// guild.cpp guild_check_member: players who claim this guild but are not in its member list lose it.
fn guild_check_member(c: &Ctx, g: &MmoGuild) {
    for p in all_users(c) {
        if guild_id_of(c, p) != g.guild_id {
            continue;
        }
        let cid = pc::get(c, p).map(|x| x.char_id).unwrap_or(0);
        if guild_getindex(g, p, cid).is_none() {
            set_sd_guild(c, p, 0);
            set_guild_id(c, p, 0);
            set_emblem_id(c, p, 0);
            log::warn!("guild: check_member {} [{}] is not member", p, name_of(c, p));
        }
    }
}

/// guild.cpp guild_recv_noinfo: the char server does not know this guild.
pub fn guild_recv_noinfo(c: &Ctx, guild_id: i32) {
    for p in all_users(c) {
        if guild_id_of(c, p) == guild_id {
            set_guild_id(c, p, 0); // erase guild
        }
    }
}

/// guild.cpp guild_recv_info: the char server's copy of a guild arrived.
pub fn guild_recv_info(c: &Ctx, sg: &MmoGuild) {
    ensure_timers(c);
    let gid = sg.guild_id;
    let (before, guild_new) = match guild_search(c, gid) {
        None => {
            put_guild(c, &MapGuild { guild_id: gid as u32, guild: MmoGuild::empty(), instance_id: 0, chargeshout_flag_id: 0 });
            // Perform the check on the user because the first load
            guild_check_member(c, sg);
            if let Some(sd) = pc::nick2sd(c, &sg.master, false) {
                // (RENEWAL: no guild_skill_relog_delay block here)
                // Also set the guild master flag.
                let mut x = gx(c, sd.id);
                x.gmaster_flag = true;
                put_gx(c, &x);
                crate::clif_guild::clif_guild_masterormember(c, &sd);
            }
            (sg.clone(), true)
        }
        Some(g) => (g.guild.clone(), false),
    };
    let Some(mut g) = guild_search(c, gid) else { return };
    g.guild = sg.clone();
    if g.guild.max_member as usize > MAX_GUILD {
        log::error!("guild_recv_info: Received guild with {} members, but MAX_GUILD is only {}. Extra guild-members have been lost!", g.guild.max_member, MAX_GUILD);
        g.guild.max_member = MAX_GUILD as i16;
    }
    let (mut bm, mut m) = (0, 0);
    for i in 0..g.guild.max() {
        if g.guild.member[i].account_id > 0 {
            let (a, ch) = (g.guild.member[i].account_id, g.guild.member[i].char_id);
            g.guild.member[i].sd = guild_sd_check(c, gid, a, ch);
            m += 1;
        } else {
            g.guild.member[i].sd = 0;
        }
        if before.member.get(i).map(|x| x.account_id > 0).unwrap_or(false) {
            bm += 1;
        }
    }
    put_guild(c, &g);
    // Transmission of information at all members
    for i in 0..g.guild.max() {
        let aid = g.guild.member[i].sd;
        if aid == 0 {
            continue;
        }
        let Some(g) = guild_search(c, gid) else { return };
        if gx(c, aid).guild != gid as u32 {
            set_sd_guild(c, aid, gid as u32);
            if let Some(sd) = unit::get(c, aid) {
                clif::name_area(c, &sd);
            }
        }
        let Some(sd) = unit::get(c, aid) else { continue };
        // (channel_config.ally_tmpl autojoin is off: no channel_gjoin(sd, 3))
        if before.guild_lv != g.guild.guild_lv || bm != m || before.max_member != g.guild.max_member {
            crate::clif_guild::clif_guild_basicinfo(c, &sd); // Submit basic information
            crate::clif_guild::clif_guild_emblem(c, &sd, &g.guild); // Submit emblem
        }
        if bm != m {
            // Send members information
            crate::clif_guild::clif_guild_memberlist(c, &sd);
        }
        if before.skill_point != g.guild.skill_point {
            crate::clif_guild::clif_guild_skillinfo(c, &sd); // Submit information skills
        }
        if guild_new {
            // Send information and affiliation if unsent (PACKETVER >= 20200902: the basic info for alt+a first)
            crate::clif_guild::clif_guild_basicinfo(c, &sd);
            crate::clif_guild::clif_guild_belonginfo(c, &sd);
            crate::clif_guild::clif_guild_notice(c, &sd);
            set_emblem_id(c, aid, g.guild.emblem_id);
        }
        if g.instance_id > 0 {
            crate::instance::instance_reqinfo(c, aid, g.instance_id);
        }
    }
    // Occurrence of an event
    let mut evs: Vec<GuildInfoEvent> = c.db().guild_infoevent().by_guild().filter(gid).collect();
    evs.sort_by_key(|e| std::cmp::Reverse(e.seq));
    for e in evs.iter() {
        c.db().guild_infoevent().id().delete(e.id);
    }
    for e in evs {
        crate::script::npc_run::npc_event_do(c, &e.name);
    }
}

/// guild.cpp guild_invite: `aid` invites `tsd`.
pub fn guild_invite(c: &Ctx, aid: u32, tsd: Option<u32>) -> bool {
    // No nullpo_retr, because its valid that target players might not exist or are not online
    let Some(t) = tsd else { return false };
    let Some(sd) = unit::get(c, aid) else { return false };
    let Some(g) = sd_guild(c, aid) else { return false };
    if g.instance_id != 0 && instance_block_invite() {
        return false;
    }
    // Guild locked.
    if crate::npc::mapflag(c, sd.map, MF_GUILDLOCK) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(228)); // Guild modification is disabled on this map.
        return false;
    }
    let Some(tu) = unit::get(c, t) else { return false };
    // @noask [LuzZza]
    if crate::social::sx(c, t).noask {
        crate::social::clif_noask_sub(c, &sd, &tu, 395); // Autorejected guild invite from %s.
        return false;
    }
    // Players in a clan can not join a guild
    if in_clan(c, t) {
        return false; // TODO: message?
    }
    // Invite permission.
    if !guild_has_permission(c, aid, GUILD_PERM_INVITE) {
        return false; // TODO: message?
    }
    // Checking if there no other invitation pending: invite_request_check is on (its tsd->party_invite / trading /
    // adopt_invite branch is dead)
    let _ = invite_request_check();
    // You can't invite someone who has already disconnected.
    if !session_active(c, t) {
        crate::clif_guild::clif_guild_inviteack(c, &sd, 1);
        return false;
    }
    // Can't invite people inside castles. [Skotlex]
    if guild_id_of(c, t) > 0 || gx(c, t).guild_invite > 0 || crate::npc::mapflag_gvg2(c, tu.map) {
        crate::clif_guild::clif_guild_inviteack(c, &sd, 0);
        return false;
    }
    // search an empty spot in guild
    if !(0..g.guild.max()).any(|i| g.guild.member[i].account_id == 0) {
        crate::clif_guild::clif_guild_inviteack(c, &sd, 3);
        return false;
    }
    let mut tx = gx(c, t);
    tx.guild_invite = guild_id_of(c, aid);
    tx.guild_invite_account = aid;
    put_gx(c, &tx);
    crate::clif_guild::clif_guild_invite(c, &tu, &g.guild);
    true
}

/// guild.cpp guild_reply_invite (flag 0 rejected, 1 accepted).
pub fn guild_reply_invite(c: &Ctx, aid: u32, guild_id: i32, flag: i32) -> bool {
    let mut x = gx(c, aid);
    // subsequent requests may override the value
    if x.guild_invite != guild_id {
        return false; // mismatch
    }
    // look up the person who sent the invite (might have logged off in the meantime)
    let tsd = id2sd(c, x.guild_invite_account).and_then(|a| unit::get(c, a));
    let mut flag = flag;
    // Already in another guild.
    if guild_id_of(c, aid) > 0 {
        flag = 0; // Set the flag to rejected, no matter what
    }
    // rejected
    if flag == 0 {
        x.guild_invite = 0;
        x.guild_invite_account = 0;
        put_gx(c, &x);
        if let Some(t) = &tsd {
            crate::clif_guild::clif_guild_inviteack(c, t, 1);
        }
        return true;
    }
    // accepted
    let Some(g) = guild_search(c, guild_id) else {
        x.guild_invite = 0;
        x.guild_invite_account = 0;
        put_gx(c, &x);
        return false;
    };
    if g.instance_id != 0 && instance_block_invite() {
        x.guild_invite = 0;
        x.guild_invite_account = 0;
        put_gx(c, &x);
        return false;
    }
    if !(0..g.guild.max()).any(|i| g.guild.member[i].account_id == 0) {
        x.guild_invite = 0;
        x.guild_invite_account = 0;
        put_gx(c, &x);
        if let Some(t) = &tsd {
            crate::clif_guild::clif_guild_inviteack(c, t, 3);
        }
        return true;
    }
    let Some(m) = guild_makemember(c, aid) else { return false };
    intif::send(Inter::GuildAddMember { guild_id, member: m });
    // TODO: send a minimap update to this player
    true
}

/// guild.cpp guild_member_joined (pc_reg_received): the player is online and belongs to a guild.
pub fn guild_member_joined(c: &Ctx, aid: u32) {
    let gid = guild_id_of(c, aid);
    let Some(mut g) = guild_search(c, gid) else {
        guild_request_info(gid);
        return;
    };
    if name_of(c, aid) == g.guild.master {
        // set the Guild Master flag (RENEWAL: no guild_skill_relog_delay block)
        let mut x = gx(c, aid);
        x.gmaster_flag = true;
        put_gx(c, &x);
    }
    let cid = pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    match guild_getindex(&g.guild, aid, cid) {
        None => set_guild_id(c, aid, 0),
        Some(i) => {
            g.guild.member[i].sd = aid;
            put_guild(c, &g);
            set_sd_guild(c, aid, gid as u32);
            if let Some(sd) = unit::get(c, aid) {
                clif::name_area(c, &sd);
            }
            // (channel_config.ally_tmpl autojoin is off: no channel_gjoin(sd, 3))
        }
    }
}

/// guild.cpp guild_member_added (the char server's answer; flag 1 = failure).
pub fn guild_member_added(c: &Ctx, guild_id: i32, account_id: u32, char_id: u32, flag: u8) {
    let Some(g) = guild_search(c, guild_id) else { return };
    let sd = id2sd(c, account_id);
    let mut x = gx(c, account_id);
    if sd.is_none() || x.guild_invite == 0 {
        // cancel if player not present or invalide guild_id invitation
        if flag == 0 {
            log::error!("guild: member added error {account_id} is not online");
            intif_guild_leave(guild_id, account_id, char_id, 0, "** Data Error **");
        }
        return;
    }
    let sd2 = id2sd(c, x.guild_invite_account).and_then(|a| unit::get(c, a));
    x.guild_invite = 0;
    x.guild_invite_account = 0;
    put_gx(c, &x);
    if flag == 1 {
        // failure
        if let Some(s2) = &sd2 {
            crate::clif_guild::clif_guild_inviteack(c, s2, 3);
        }
        return;
    }
    // if all ok add player to guild
    set_guild_id(c, account_id, g.guild.guild_id);
    set_emblem_id(c, account_id, g.guild.emblem_id);
    set_sd_guild(c, account_id, g.guild_id);
    let Some(su) = unit::get(c, account_id) else { return };
    // Packets which were sent in the previous 'guild_sent' implementation.
    crate::clif_guild::clif_guild_belonginfo(c, &su);
    crate::clif_guild::clif_guild_notice(c, &su);
    // Send emblem update to self and people around
    crate::clif_guild::clif_guild_emblem_area(c, &su);
    if let Some(s2) = &sd2 {
        crate::clif_guild::clif_guild_inviteack(c, s2, 2);
    }
    // Update display name
    clif::name_area(c, &su);
    if g.instance_id > 0 {
        crate::instance::instance_reqinfo(c, account_id, g.instance_id);
    }
}

/// guild.cpp guild_leave: the player asks to leave.
pub fn guild_leave(c: &Ctx, aid: u32, guild_id: i32, account_id: u32, char_id: u32, mes: &str) -> bool {
    let Some(sd) = unit::get(c, aid) else { return false };
    let Some(g) = sd_guild(c, aid) else { return false };
    if g.instance_id > 0 && instance_block_leave() {
        return false;
    }
    if crate::npc::mapflag(c, sd.map, MF_GUILDLOCK) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(228)); // Guild modification is disabled on this map.
        return false;
    }
    if crate::battleground::bg_id_of(c, aid) != 0 {
        clif::displaymessage(c, &sd, crate::data::msg_txt(670)); // You can't leave battleground guilds.
        return false;
    }
    let cid = pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    let my_gid = guild_id_of(c, aid);
    if aid != account_id || cid != char_id || my_gid != guild_id || crate::npc::mapflag_gvg2(c, sd.map) {
        return false;
    }
    // guild_trade_bound_cancel(sd): BOUND_ITEMS cancels a trade of guild-bound items (guild.cpp:775-780)
    crate::trade::trade_bound_cancel(c, aid, crate::inventory::BOUND_GUILD);
    intif_guild_leave(my_gid, aid, cid, 0, mes)
}

/// guild.cpp guild_expulsion: `aid` expels <account_id, char_id>.
pub fn guild_expulsion(c: &Ctx, aid: u32, guild_id: i32, account_id: u32, char_id: u32, mes: &str) -> bool {
    let Some(sd) = unit::get(c, aid) else { return false };
    let Some(g) = sd_guild(c, aid) else { return false };
    if guild_id_of(c, aid) != guild_id {
        return false;
    }
    if !guild_has_permission(c, aid, GUILD_PERM_EXPEL) {
        return false;
    }
    if g.instance_id > 0 && instance_block_expulsion() {
        return false;
    }
    // TODO: for leave this is different messages (rAthena's own comment)
    if crate::battleground::bg_id_of(c, aid) != 0 || crate::npc::mapflag(c, sd.map, MF_GUILDLOCK) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(228)); // Guild modification is disabled on this map.
        return false;
    }
    // Can't leave inside guild castles.
    if let Some(t) = pc::get(c, account_id) {
        if t.char_id == char_id && unit::get(c, account_id).map(|u| crate::npc::mapflag_gvg2(c, u.map)).unwrap_or(false) {
            return false;
        }
    }
    // find the member and perform expulsion
    let Some(i) = guild_getindex(&g.guild, account_id, char_id) else { return false };
    // Can't expel the guild leader
    if g.guild.member[i].name == g.guild.master {
        return false;
    }
    // guild_trade_bound_cancel(*tsd) when tsd (map_id2sd(account_id)) is online (BOUND_ITEMS)
    if pc::get(c, account_id).is_some() {
        crate::trade::trade_bound_cancel(c, account_id, crate::inventory::BOUND_GUILD);
    }
    intif_guild_leave(g.guild.guild_id, account_id, char_id, 1, mes)
}

/// guild.cpp guild_member_withdraw: the char server confirms that a member left (flag 0) or was expelled (1).
pub fn guild_member_withdraw(c: &Ctx, guild_id: i32, account_id: u32, char_id: u32, flag: u8, name: &str, mes: &str) {
    let Some(g) = guild_search(c, guild_id) else { return }; // no such guild (error!)
    let sd = pc::charid2sd(c, char_id).map(|u| u.id);
    let Some(i) = guild_getindex(&g.guild, account_id, char_id) else { return }; // not a member (inconsistency!)
    // Guild bound item check
    crate::guild_storage::guild_retrieveitembound(c, char_id, account_id, guild_id);
    let Some(g) = guild_search(c, guild_id) else { return };
    let Some(online) = guild_getavailablesd(&g.guild).and_then(|a| unit::get(c, a)) else {
        return; // noone online to inform
    };
    if flag == 0 {
        crate::clif_guild::clif_guild_leave(c, &online, name, char_id, mes);
    } else {
        crate::clif_guild::clif_guild_expulsion(c, &online, name, char_id, mes);
    }
    // remove member from guild
    let Some(mut g) = guild_search(c, guild_id) else { return };
    g.guild.member[i] = GuildMember::default();
    put_guild(c, &g);
    crate::clif_guild::clif_guild_memberlist(c, &online);
    // update char, if online
    let Some(sd) = sd else { return };
    if guild_id_of(c, sd) != guild_id {
        return;
    }
    // do stuff that needs the guild_id first, BEFORE we wipe it
    if pc::get(c, sd).map(|p| p.storage_flag == 2).unwrap_or(false) {
        crate::guild_storage::storage_guild_storageclose(c, sd); // Close the guild storage.
    }
    if let Some(su) = unit::get(c, sd) {
        guild_send_dot_remove(c, &su);
    }
    crate::channel::channel_pcquit(c, sd, 3); // leave guild and ally chan
    set_guild_id(c, sd, 0);
    set_sd_guild(c, sd, 0);
    set_emblem_id(c, sd, 0);
    if g.instance_id != 0 {
        // User was on the instance map of the guild
        let m = unit::get(c, sd).map(|u| u.map).unwrap_or(0);
        if g.instance_id == crate::instance_map::map_instance_id(c, m) {
            crate::instance_map::pc_setpos_savepoint(c, sd, crate::unit::CLR_TELEPORT);
        }
    }
    if let Some(su) = unit::get(c, sd) {
        clif::name_area(c, &su); // Update display name [Skotlex]
    }
    end_guild_scs(c, sd);
    // Send emblem update to self and people around
    if let Some(su) = unit::get(c, sd) {
        crate::clif_guild::clif_guild_emblem_area(c, &su);
    }
}

/// guild.cpp guild_send_memberinfoshort: the player logs in (1, LoadEndAck) or out (0, unit_free).
pub fn guild_send_memberinfoshort(c: &Ctx, aid: u32, online: bool) {
    if guild_id_of(c, aid) <= 0 {
        return;
    }
    let Some(mut g) = sd_guild(c, aid) else { return };
    let Some(ch) = pc::character_of(c, aid) else { return };
    intif::send(Inter::GuildMemberInfoShort { guild_id: g.guild.guild_id, account_id: aid, char_id: ch.id, online: online as u8, lv: ch.base_level, class: ch.class });
    if !online {
        match guild_getindex(&g.guild, aid, ch.id) {
            Some(i) => {
                g.guild.member[i].sd = 0;
                put_guild(c, &g);
            }
            None => log::error!("guild_send_memberinfoshort: Failed to locate member {aid}:{} in guild {}!", ch.id, g.guild.guild_id),
        }
        return;
    }
    if pc::get(c, aid).map(|p| p.connect_new).unwrap_or(false) {
        // Note that this works because it is invoked in parse_LoadEndAck before connect_new is cleared.
        let Some(sd) = unit::get(c, aid) else { return };
        // (PACKETVER >= 20200902: the basic info for alt+a)
        crate::clif_guild::clif_guild_basicinfo(c, &sd);
        crate::clif_guild::clif_guild_belonginfo(c, &sd);
        set_emblem_id(c, aid, g.guild.emblem_id);
    }
}

/// guild.cpp guild_recv_memberinfoshort: the char server's notice of a member's login / logout / level.
pub fn guild_recv_memberinfoshort(c: &Ctx, guild_id: i32, account_id: u32, char_id: u32, online: u8, lv: u16, class: u16) {
    let Some(mut g) = guild_search(c, guild_id) else { return };
    let (mut alv, mut cnt, mut om) = (0i32, 0i32, 0i16);
    let mut idx: Option<usize> = None;
    let mut oldonline = -1i16;
    for i in 0..g.guild.max() {
        let m = &mut g.guild.member[i];
        if m.account_id == 0 {
            continue;
        }
        if m.account_id == account_id && m.char_id == char_id {
            oldonline = m.online;
            m.online = online as i16;
            m.lv = lv as i16;
            m.class = class as i16;
            idx = Some(i);
        }
        alv += m.lv as i32;
        cnt += 1;
        if m.online != 0 {
            om += 1;
        }
    }
    let Some(idx) = idx.filter(|_| cnt != 0) else {
        // Treat char_id who doesn't match guild_id (not found as member)
        if let Some(p) = pc::get(c, account_id) {
            if p.char_id == char_id {
                set_guild_id(c, account_id, 0);
                set_emblem_id(c, account_id, 0);
            }
        }
        log::warn!("guild: not found member {account_id},{char_id} on {guild_id}[{}]", g.guild.name);
        return;
    };
    g.guild.average_lv = (alv / cnt) as i16;
    g.guild.connect_member = om;
    // Ensure validity of pointer (ie: player logs in/out, changes map-server)
    g.guild.member[idx].sd = guild_sd_check(c, guild_id, account_id, char_id);
    put_guild(c, &g);
    let sdv = g.guild.member[idx].sd;
    if oldonline != online as i16 {
        crate::clif_guild::clif_guild_memberlogin_notice(c, &g.guild, idx, online as u32);
    }
    if sdv == 0 {
        return;
    }
    // Send XY dot updates. [Skotlex]
    let Some(me) = unit::get(c, sdv) else { return };
    for i in 0..g.guild.max() {
        let other = g.guild.member[i].sd;
        if other == 0 || i == idx {
            continue;
        }
        let Some(ou) = unit::get(c, other) else { continue };
        if ou.map != me.map {
            continue;
        }
        crate::clif_guild::clif_guild_xy_single(c, &me, &ou);
        crate::clif_guild::clif_guild_xy_single(c, &ou, &me);
    }
}

/// guild.cpp guild_send_message (intif_guild_message goes nowhere: there is no other map server).
pub fn guild_send_message(c: &Ctx, aid: u32, mes: &[u8]) {
    let gid = guild_id_of(c, aid);
    if gid == 0 {
        return;
    }
    guild_recv_message(c, gid, aid, mes);
    // Chat logging type 'G' / Guild Chat
    crate::map_log::log_chat_sd(c, crate::map_log::LOG_CHAT_GUILD, gid, aid, None, &crate::net::wire::from_latin1(crate::net::wire::until_nul(mes)));
}

/// guild.cpp guild_recv_message.
pub fn guild_recv_message(c: &Ctx, guild_id: i32, _account_id: u32, mes: &[u8]) {
    let Some(g) = guild_search(c, guild_id) else { return };
    crate::clif_guild::clif_guild_message(c, &g.guild, mes);
}

/// guild.cpp guild_change_memberposition (intif GMI_POSITION).
pub fn guild_change_memberposition(guild_id: i32, account_id: u32, char_id: u32, idx: i16) {
    intif_guild_change_memberinfo(guild_id, account_id, char_id, GMI_POSITION, idx as i64);
}

/// guild.cpp guild_memberposition_changed.
pub fn guild_memberposition_changed(c: &Ctx, guild_id: i32, idx: usize, pos: i32) {
    let Some(mut g) = guild_search(c, guild_id) else { return };
    g.guild.member[idx].position = pos as i16;
    put_guild(c, &g);
    crate::clif_guild::clif_guild_memberpositionchanged(c, &g.guild, idx);
    // Update char position in client [LuzZza]
    let sd = g.guild.member[idx].sd;
    if sd != 0 {
        if let Some(su) = unit::get(c, sd) {
            crate::clif_guild::clif_guild_position_selected(c, &su);
        }
    }
}

/// guild.cpp guild_change_position: a position's title / rights / tax (mode & GUILD_PERM_ALL, exp_mode capped to
/// guild_exp_limit). `modified` is left as the stack holds it in rAthena; the char server overwrites it.
pub fn guild_change_position(guild_id: i32, idx: i32, mode: i32, exp_mode: i32, name: &str) {
    let exp_mode = exp_mode.clamp(0, guild_exp_limit());
    let p = GuildPosition { name: trunc(name, NAME_LENGTH), mode: mode & GUILD_PERM_ALL, exp_mode, modified: 0 };
    intif::send(Inter::GuildPosition { guild_id, idx, pos: p });
}

/// guild.cpp guild_position_changed.
pub fn guild_position_changed(c: &Ctx, guild_id: i32, idx: i32, p: &GuildPosition) {
    let Some(mut g) = guild_search(c, guild_id) else { return };
    if idx < 0 || idx as usize >= MAX_GUILDPOSITION {
        return;
    }
    g.guild.position[idx as usize] = p.clone();
    put_guild(c, &g);
    crate::clif_guild::clif_guild_positionchanged(c, &g.guild, idx as usize);
    // Update char name in client [LuzZza]
    for i in 0..g.guild.max() {
        let m = &g.guild.member[i];
        if m.position as i32 == idx && m.sd != 0 {
            if let Some(su) = unit::get(c, m.sd) {
                crate::clif_guild::clif_guild_position_selected(c, &su);
            }
        }
    }
}

/// guild.cpp guild_change_notice.
pub fn guild_change_notice(c: &Ctx, aid: u32, guild_id: i32, mes1: &[u8], mes2: &[u8]) {
    if guild_id != guild_id_of(c, aid) {
        return;
    }
    let cut = |b: &[u8], n: usize| crate::net::wire::from_latin1(&b.iter().copied().take_while(|&x| x != 0).take(n - 1).collect::<Vec<u8>>());
    // intif_guild_notice: safestrncpy(mes1, MAX_GUILDMES1), safestrncpy(mes2, MAX_GUILDMES2)
    intif::send(Inter::GuildNotice { guild_id, mes1: cut(mes1, MAX_GUILDMES1), mes2: cut(mes2, MAX_GUILDMES2) });
}

/// guild.cpp guild_notice_changed.
pub fn guild_notice_changed(c: &Ctx, guild_id: i32, mes1: &str, mes2: &str) {
    let Some(mut g) = guild_search(c, guild_id) else { return };
    g.guild.mes1 = mes1.to_string();
    g.guild.mes2 = mes2.to_string();
    put_guild(c, &g);
    for i in 0..g.guild.max() {
        let sd = g.guild.member[i].sd;
        if sd != 0 {
            if let Some(su) = unit::get(c, sd) {
                crate::clif_guild::clif_guild_notice(c, &su);
            }
        }
    }
}

/// guild.cpp guild_check_emblem_change_condition (require_glory_guild is off).
fn guild_check_emblem_change_condition(c: &Ctx, aid: u32) -> bool {
    if require_glory_guild() {
        if let Some(g) = sd_guild(c, aid) {
            if guild_checkskill(&g.guild, GD_GLORYGUILD as i32) > 0 {
                if let Some(su) = unit::get(c, aid) {
                    crate::skill::skill_fail(c, &su, GD_GLORYGUILD, 0);
                }
                return false;
            }
        }
    }
    true
}

/// guild.cpp guild_change_emblem (intif_guild_emblem refuses a guild id <= 0 or a length above 2000).
pub fn guild_change_emblem(c: &Ctx, aid: u32, data: &[u8]) -> i32 {
    if !guild_check_emblem_change_condition(c, aid) {
        return 0;
    }
    let gid = guild_id_of(c, aid);
    if gid <= 0 || data.len() > 2000 {
        return 0;
    }
    intif::send(Inter::GuildEmblem { guild_id: gid, data: data.to_vec() });
    1
}

/// guild.cpp guild_change_emblem_version.
pub fn guild_change_emblem_version(c: &Ctx, aid: u32, version: i32) -> i32 {
    if !guild_check_emblem_change_condition(c, aid) {
        return 0;
    }
    let gid = guild_id_of(c, aid);
    if gid <= 0 {
        return 0;
    }
    intif::send(Inter::GuildEmblemVersion { guild_id: gid, version });
    1
}

/// guild.cpp guild_emblem_changed (data None: only the version changed).
pub fn guild_emblem_changed(c: &Ctx, guild_id: i32, emblem_id: i32, data: Option<&[u8]>) {
    let Some(mut g) = guild_search(c, guild_id) else { return };
    let len = data.map(|d| d.len()).unwrap_or(0);
    if let Some(d) = data {
        g.guild.emblem_data = d.to_vec();
    }
    g.guild.emblem_len = len as i32;
    g.guild.emblem_id = emblem_id;
    put_guild(c, &g);
    for i in 0..g.guild.max() {
        let sd = g.guild.member[i].sd;
        if sd == 0 {
            continue;
        }
        set_emblem_id(c, sd, emblem_id);
        let Some(su) = unit::get(c, sd) else { continue };
        crate::clif_guild::clif_guild_belonginfo(c, &su);
        crate::clif_guild::clif_guild_emblem(c, &su, &g.guild);
        crate::clif_guild::clif_guild_emblem_area(c, &su);
    }
    // update guardians (mobs) of the guild's castles and the npcs that flagemblem attached to this guild
    crate::woe::guild_emblem_changed_castles(c, guild_id, emblem_id);
}

/// guild.cpp guild_payexp: the part of `exp` the player's position taxes for the guild (cached for
/// guild_payexp_timer). pc_gainexp subtracts it from the base EXP.
pub fn guild_payexp(c: &Ctx, aid: u32, exp: u64) -> u64 {
    if exp == 0 {
        return 0;
    }
    let gid = guild_id_of(c, aid);
    let Some(g) = sd_guild(c, aid) else { return 0 };
    if gid == 0 {
        return 0;
    }
    let per = guild_getposition(c, aid);
    if per < 0 {
        return 0;
    }
    let per = g.guild.position[per as usize].exp_mode;
    if per < 1 {
        return 0;
    }
    let mut exp = exp;
    if per < 100 {
        exp = exp.wrapping_mul(per as u64) / 100;
    }
    // Otherwise tax everything.
    guild_expcache_add(c, aid, gid, exp);
    exp
}

/// guild.cpp guild_getexp: `exp` (guildgetexp) goes to the guild through the EXP cache.
pub fn guild_getexp(c: &Ctx, aid: u32, exp: u64) -> u64 {
    let gid = guild_id_of(c, aid);
    if gid == 0 || sd_guild(c, aid).is_none() {
        return 0;
    }
    guild_expcache_add(c, aid, gid, exp);
    exp
}

/// guild_expcache_db->ensure(char_id, create_expcache) and c->exp = safe_addition_cap(c->exp, exp, MAX_GUILD_EXP).
fn guild_expcache_add(c: &Ctx, aid: u32, guild_id: i32, exp: u64) {
    ensure_timers(c);
    let cid = pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    let existing = c.db().guild_expcache().char_id().find(cid);
    let mut row = existing.clone().unwrap_or(GuildExpCache { char_id: cid, guild_id, account_id: aid, exp: 0 });
    row.exp = row.exp.saturating_add(exp).min(MAX_GUILD_EXP);
    if existing.is_some() {
        c.db().guild_expcache().char_id().update(row);
    } else {
        c.db().guild_expcache().insert(row);
    }
}

/// guild.cpp guild_skillup (pc_skillup for a guild skill): only the guild master, with skill points left.
pub fn guild_skillup(c: &Ctx, aid: u32, skill_id: u16) {
    let idx = guild_skill_get_index(skill_id as i32);
    if idx == -1 {
        return;
    }
    let Some(g) = sd_guild(c, aid) else { return };
    if guild_id_of(c, aid) == 0 || name_of(c, aid) != g.guild.master {
        return; // no guild / not the guild master
    }
    let max = crate::db::skills::guild_skill_get_max(skill_id) as i32;
    let s = &g.guild.skill[idx as usize];
    if g.guild.skill_point > 0 && s.id != 0 && s.lv < max {
        intif::send(Inter::GuildSkillUp { guild_id: g.guild.guild_id, skill_id, account_id: aid, max });
    }
}

/// guild.cpp guild_skillupack: the char server raised the skill.
pub fn guild_skillupack(c: &Ctx, guild_id: i32, skill_id: u16, account_id: u32) {
    let Some(g) = guild_search(c, guild_id) else { return };
    let idx = guild_skill_get_index(skill_id as i32);
    if idx == -1 {
        return;
    }
    if let Some(sd) = id2sd(c, account_id).and_then(|a| unit::get(c, a)) {
        let lv = g.guild.skill[idx as usize].lv as u16;
        let range = crate::db::skills::skill_get_range(skill_id, lv);
        crate::clif_guild::clif_skillup(c, &sd, skill_id, lv, range, true);
        /* Guild Aura handling */
        if is_guild_aura(skill_id) {
            guild_guildaura_refresh(c, sd.id, skill_id, lv);
        }
    }
    // Inform all members
    for i in 0..g.guild.max() {
        let sd = g.guild.member[i].sd;
        if sd != 0 {
            if let Some(su) = unit::get(c, sd) {
                crate::clif_guild::clif_guild_skillinfo(c, &su);
            }
        }
    }
}

/// guild.cpp guild_reqalliance: `aid` asks `tsd` for an alliance between their guilds.
pub fn guild_reqalliance(c: &Ctx, aid: u32, tsd: Option<u32>) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    if is_agit_start(c) {
        // Disable alliance creation during woe [Valaris]
        clif::displaymessage(c, &sd, crate::data::msg_txt(676)); // Alliances cannot be made during Guild Wars!
        return 0;
    }
    let Some(t) = tsd else { return 0 };
    if guild_id_of(c, t) <= 0 {
        return 0;
    }
    // Check, is tsd guild master, if not - cancel alliance. [f0und3r]
    if guild_alliance_onlygm() && !gx(c, t).gmaster_flag {
        crate::clif_guild::clif_guild_allianceack(c, &sd, 5);
        return 0;
    }
    let (Some(g), Some(tg)) = (sd_guild(c, aid), sd_guild(c, t)) else { return 0 };
    // Prevent creation alliance with same guilds [LuzZza]
    if guild_id_of(c, aid) == guild_id_of(c, t) {
        return 0;
    }
    if guild_get_alliance_count(&g.guild, 0) >= max_guild_alliance() {
        crate::clif_guild::clif_guild_allianceack(c, &sd, 4);
        return 0;
    }
    if guild_get_alliance_count(&tg.guild, 0) >= max_guild_alliance() {
        crate::clif_guild::clif_guild_allianceack(c, &sd, 3);
        return 0;
    }
    let mut tx = gx(c, t);
    if tx.guild_alliance > 0 {
        crate::clif_guild::clif_guild_allianceack(c, &sd, 1);
        return 0;
    }
    let tgid = guild_id_of(c, t);
    // check if already allied
    if (0..MAX_GUILDALLIANCE).any(|i| g.guild.alliance[i].guild_id == tgid && g.guild.alliance[i].opposition == 0) {
        crate::clif_guild::clif_guild_allianceack(c, &sd, 0);
        return 0;
    }
    tx.guild_alliance = guild_id_of(c, aid);
    tx.guild_alliance_account = aid;
    put_gx(c, &tx);
    if let Some(tu) = unit::get(c, t) {
        crate::clif_guild::clif_guild_reqalliance(c, &tu, aid, &g.guild.name);
    }
    0
}

/// guild.cpp guild_reply_reqalliance: `aid` answers the alliance request of `account_id` (flag 1 consent).
pub fn guild_reply_reqalliance(c: &Ctx, aid: u32, account_id: u32, flag: i32) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let Some(t) = id2sd(c, account_id) else {
        // Character left? Cancel alliance.
        crate::clif_guild::clif_guild_allianceack(c, &sd, 3);
        return 0;
    };
    let Some(tu) = unit::get(c, t) else { return 0 };
    let mut x = gx(c, aid);
    let tgid = guild_id_of(c, t);
    if x.guild_alliance != tgid {
        return 0; // proposed guild_id alliance doesn't match tsd guildid
    }
    let gid = guild_id_of(c, aid);
    if flag == 1 {
        // consent
        let g = sd_guild(c, aid);
        let tg = sd_guild(c, t);
        let Some(g) = g.filter(|g| guild_get_alliance_count(&g.guild, 0) < max_guild_alliance()) else {
            crate::clif_guild::clif_guild_allianceack(c, &sd, 4);
            crate::clif_guild::clif_guild_allianceack(c, &tu, 3);
            return 0;
        };
        let Some(tg) = tg.filter(|g| guild_get_alliance_count(&g.guild, 0) < max_guild_alliance()) else {
            crate::clif_guild::clif_guild_allianceack(c, &sd, 3);
            crate::clif_guild::clif_guild_allianceack(c, &tu, 4);
            return 0;
        };
        for i in 0..MAX_GUILDALLIANCE {
            if g.guild.alliance[i].guild_id == tgid && g.guild.alliance[i].opposition == 1 {
                intif_guild_alliance(gid, tgid, aid, t, 9);
            }
        }
        for i in 0..MAX_GUILDALLIANCE {
            if tg.guild.alliance[i].guild_id == gid && tg.guild.alliance[i].opposition == 1 {
                intif_guild_alliance(tgid, gid, t, aid, 9);
            }
        }
        // inform other servers
        intif_guild_alliance(gid, tgid, aid, t, 0);
    } else {
        // deny
        x.guild_alliance = 0;
        x.guild_alliance_account = 0;
        put_gx(c, &x);
        crate::clif_guild::clif_guild_allianceack(c, &tu, 3);
    }
    0
}

/// guild.cpp guild_delalliance: `aid` breaks the alliance (flag 0) / opposition (1) with `guild_id`.
pub fn guild_delalliance(c: &Ctx, aid: u32, guild_id: i32, flag: i32) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    if is_agit_start(c) {
        // Disable alliance breaking during woe [Valaris]
        clif::displaymessage(c, &sd, crate::data::msg_txt(677)); // Alliances cannot be broken during Guild Wars!
        return 0;
    }
    intif_guild_alliance(guild_id_of(c, aid), guild_id, aid, 0, flag | 8);
    0
}

/// guild.cpp guild_opposition: `aid` sets `tsd`'s guild as an enemy.
pub fn guild_opposition(c: &Ctx, aid: u32, tsd: Option<u32>) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let Some(g) = sd_guild(c, aid) else { return 0 };
    let Some(t) = tsd else { return 0 };
    let (gid, tgid) = (guild_id_of(c, aid), guild_id_of(c, t));
    // Prevent creation opposition with same guilds [LuzZza]
    if gid == tgid {
        return 0;
    }
    if guild_get_alliance_count(&g.guild, 1) >= max_guild_alliance() {
        crate::clif_guild::clif_guild_oppositionack(c, &sd, 1);
        return 0;
    }
    for i in 0..MAX_GUILDALLIANCE {
        // checking relations
        if g.guild.alliance[i].guild_id == tgid {
            if g.guild.alliance[i].opposition == 1 {
                // check if not already hostile
                crate::clif_guild::clif_guild_oppositionack(c, &sd, 2);
                return 0;
            }
            if is_agit_start(c) {
                return 0; // Prevent the changing of alliances to oppositions during WoE.
            }
            // Change alliance to opposition.
            intif_guild_alliance(gid, tgid, aid, t, 8);
        }
    }
    // inform other serv
    intif_guild_alliance(gid, tgid, aid, t, 1);
    0
}

/// guild.cpp guild_allianceack: the char server's notice of a new / removed relationship between two guilds (flag
/// & 1 opposition, & 8 removal, & 0x70 failure). Each guild is read afresh at every step: guild_id1 and guild_id2
/// may be the same guild (one object in rAthena). An unknown guild is a null g[i] (`&guild_search(id)->guild` of a
/// null pointer).
pub fn guild_allianceack(c: &Ctx, guild_id1: i32, guild_id2: i32, account_id1: u32, account_id2: u32, flag: i32, name1: &str, name2: &str) {
    let guild_id = [guild_id1, guild_id2];
    let guild_name = [name1, name2];
    let sd = [id2sd(c, account_id1), id2sd(c, account_id2)];
    let exists = [guild_search(c, guild_id1).is_some(), guild_search(c, guild_id2).is_some()];
    if let Some(s0) = sd[0] {
        if flag & 0x0f == 0 {
            let mut x = gx(c, s0);
            x.guild_alliance = 0;
            x.guild_alliance_account = 0;
            put_gx(c, &x);
        }
    }
    let n = 2 - (flag & 1) as usize;
    if flag & 0x70 != 0 {
        // failure
        for (i, s) in sd.iter().enumerate().take(n) {
            if let Some(su) = s.and_then(|a| unit::get(c, a)) {
                crate::clif_guild::clif_guild_allianceack(c, &su, if (flag >> 4) == i as i32 + 1 { 3 } else { 4 });
            }
        }
        return;
    }
    if flag & 0x08 == 0 {
        // new relationship
        for i in 0..n {
            if !exists[i] {
                continue;
            }
            let Some(mut g) = guild_search(c, guild_id[i]) else { continue };
            if let Some(j) = (0..MAX_GUILDALLIANCE).find(|&j| g.guild.alliance[j].guild_id == 0) {
                g.guild.alliance[j].guild_id = guild_id[1 - i];
                g.guild.alliance[j].name = trunc(guild_name[1 - i], NAME_LENGTH + 1);
                g.guild.alliance[j].opposition = flag & 1;
                put_guild(c, &g);
            }
        }
    } else {
        // remove relationship
        for i in 0..n {
            if exists[i] {
                if let Some(g) = guild_search(c, guild_id[i]) {
                    for j in 0..g.guild.max() {
                        let s = g.guild.member[j].sd;
                        if s != 0 {
                            crate::channel::channel_pcquit(c, s, 2); // leave all alliance chan
                        }
                    }
                }
                if let Some(mut g) = guild_search(c, guild_id[i]) {
                    if let Some(j) = (0..MAX_GUILDALLIANCE).find(|&j| g.guild.alliance[j].guild_id == guild_id[1 - i] && g.guild.alliance[j].opposition == (flag & 1)) {
                        g.guild.alliance[j].guild_id = 0;
                        put_guild(c, &g);
                    }
                }
            }
            if let Some(su) = sd[i].and_then(|a| unit::get(c, a)) {
                // notify players
                crate::clif_guild::clif_guild_delalliance(c, &su, guild_id[1 - i] as u32, (flag & 1) as u32);
            }
        }
    }
    if flag & 0x0f == 0 {
        // alliance notification
        if let Some(su) = sd[1].and_then(|a| unit::get(c, a)) {
            crate::clif_guild::clif_guild_allianceack(c, &su, 2);
        }
    } else if flag & 0x0f == 1 {
        // enemy notification
        if let Some(su) = sd[0].and_then(|a| unit::get(c, a)) {
            crate::clif_guild::clif_guild_oppositionack(c, &su, 0);
        }
    }
    for i in 0..n {
        // Retransmission of the relationship list to all members
        if !exists[i] {
            continue;
        }
        let Some(g) = guild_search(c, guild_id[i]) else { continue };
        for j in 0..g.guild.max() {
            let s = g.guild.member[j].sd;
            if s == 0 {
                continue;
            }
            if let Some(su) = unit::get(c, s) {
                crate::clif_guild::clif_guild_allianceinfo(c, &su);
            }
            // (ally autojoin is off: no channel_gjoin(sd_mem, 2))
        }
    }
}

/// guild.cpp guild_broken_sub: guild `gid` drops every relationship with the broken guild.
fn guild_broken_sub(c: &Ctx, gid: i32, broken: i32) {
    for i in 0..MAX_GUILDALLIANCE {
        let Some(mut g) = guild_search(c, gid) else { return };
        if g.guild.alliance[i].guild_id != broken {
            continue;
        }
        let opp = g.guild.alliance[i].opposition;
        for j in 0..g.guild.max() {
            let s = g.guild.member[j].sd;
            if s != 0 {
                if let Some(su) = unit::get(c, s) {
                    crate::clif_guild::clif_guild_delalliance(c, &su, broken as u32, opp as u32);
                }
            }
        }
        intif_guild_alliance(g.guild.guild_id, broken, 0, 0, opp | 8);
        g.guild.alliance[i].guild_id = 0;
        put_guild(c, &g);
    }
}

/// guild.cpp guild_broken: the char server broke the guild.
pub fn guild_broken(c: &Ctx, guild_id: i32, flag: u8) -> i32 {
    let Some(g) = guild_search(c, guild_id) else { return 0 };
    if flag != 0 {
        return 0;
    }
    for i in 0..g.guild.max() {
        // Destroy all relationships
        let sd = g.guild.member[i].sd;
        if sd == 0 {
            continue;
        }
        if pc::get(c, sd).map(|p| p.storage_flag == 2).unwrap_or(false) {
            crate::guild_storage::storage_guild_storage_quit(c, sd, 1);
        }
        set_guild_id(c, sd, 0);
        let mut x = gx(c, sd);
        x.guild = 0;
        x.gmaster_flag = false;
        put_gx(c, &x);
        // (sd->guild_emblem_id is left as it was)
        if let Some(su) = unit::get(c, sd) {
            crate::clif_guild::clif_guild_broken(c, &su, 0);
            clif::name_area(c, &su); // [LuzZza]
        }
        end_guild_scs(c, sd);
    }
    for og in guild_db_iter(c) {
        guild_broken_sub(c, og.guild.guild_id, guild_id);
    }
    crate::woe::castle_guild_broken_sub(c, guild_id);
    crate::guild_storage::storage_guild_delete(c, guild_id);
    // channel_delete(g->channel) (ally_tmpl has a name)
    let key = crate::channel::ally_key(guild_id);
    if crate::channel::get(c, &key).is_some() {
        crate::channel::channel_delete(c, &key, false);
    }
    c.db().guild_db().guild_id().delete(guild_id as u32);
    0
}

/// guild.cpp guild_gm_change: asks the char server to make the member `char_id` the guild master.
pub fn guild_gm_change(c: &Ctx, guild_id: i32, char_id: u32) -> bool {
    let Some(g) = guild_search(c, guild_id) else { return false };
    if g.instance_id > 0 && instance_block_leaderchange() {
        return false;
    }
    let Some(i) = (0..MAX_GUILD).find(|&i| g.guild.member[i].char_id == char_id) else {
        return false; // Not part of the guild
    };
    let name = g.guild.member[i].name.clone();
    // Nothing to change.
    if g.guild.master == name {
        return false;
    }
    // Notify servers that master has changed.
    let len = crate::net::wire::latin1(&name).len() + 1;
    intif::send(Inter::GuildMasterChange { guild_id, name, len });
    true
}

/// guild.cpp guild_gm_changed: the char server's notice of the new guild master.
pub fn guild_gm_changed(c: &Ctx, guild_id: i32, account_id: u32, char_id: u32, time: i64) -> i32 {
    let Some(mut g) = guild_search(c, guild_id) else { return 0 };
    let max = g.guild.max();
    let pos = (0..max).find(|&p| g.guild.member[p].account_id == account_id && g.guild.member[p].char_id == char_id).unwrap_or(max);
    if pos == 0 || pos == max {
        return 0;
    }
    g.guild.member.swap(pos, 0);
    g.guild.member[pos].position = g.guild.member[0].position;
    g.guild.member[0].position = 0; // Position 0: guild Master.
    g.guild.master = g.guild.member[0].name.clone();
    put_guild(c, &g);
    let old = g.guild.member[pos].sd;
    if old != 0 && session_active(c, old) {
        if let Some(su) = unit::get(c, old) {
            clif::displaymessage(c, &su, crate::data::msg_txt(678)); // You no longer are the Guild Master.
        }
        let mut x = gx(c, old);
        x.gmaster_flag = false;
        put_gx(c, &x);
        if let Some(su) = unit::get(c, old) {
            clif::name_area(c, &su);
        }
    }
    let new = g.guild.member[0].sd;
    if new != 0 && session_active(c, new) {
        if let Some(su) = unit::get(c, new) {
            clif::displaymessage(c, &su, crate::data::msg_txt(679)); // You have become the Guild Master!
        }
        let mut x = gx(c, new);
        x.gmaster_flag = true;
        put_gx(c, &x);
        if let Some(su) = unit::get(c, new) {
            clif::name_area(c, &su);
        }
        // (RENEWAL: no guild_block_skill)
    }
    // announce the change to all guild members
    for i in 0..g.guild.max() {
        let sd = g.guild.member[i].sd;
        if sd == 0 {
            continue;
        }
        let Some(su) = unit::get(c, sd) else { continue };
        crate::clif_guild::clif_guild_basicinfo(c, &su);
        crate::clif_guild::clif_guild_memberlist(c, &su);
        crate::clif_guild::clif_guild_belonginfo(c, &su); // Update clientside guildmaster flag
    }
    // Store changing time
    if let Some(mut g) = guild_search(c, guild_id) {
        g.guild.last_leader_change = time;
        put_guild(c, &g);
    }
    1
}

/// guild.cpp guild_break: the guild master disbands the guild (`name` must be the guild's name).
pub fn guild_break(c: &Ctx, aid: u32, name: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return 0 };
    let Some(g) = sd_guild(c, aid) else { return 0 };
    if g.guild.name != name {
        return 0;
    }
    if !gx(c, aid).gmaster_flag {
        return 0;
    }
    let cid = pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    if (0..g.guild.max()).any(|i| g.guild.member[i].account_id > 0 && (g.guild.member[i].account_id != aid || g.guild.member[i].char_id != cid)) {
        crate::clif_guild::clif_guild_broken(c, &sd, 2);
        return 0;
    }
    // Guild locked.
    if crate::npc::mapflag(c, sd.map, MF_GUILDLOCK) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(228));
        return 0;
    }
    if g.instance_id != 0 {
        if instance_block_leave() {
            return 0;
        }
        crate::instance::instance_destroy(c, g.instance_id);
    }
    /* Regardless of char server allowing it, we clear the guild master's auras */
    // (guild.cpp:2333-2353: the unit_data's skillunits in vector order, collected first, then deleted)
    let auras: Vec<i32> = crate::skillunit::data::groups_of(c, aid).iter().filter(|g| is_guild_aura(g.skill_id)).map(|g| g.group_id).collect();
    for group_id in auras {
        crate::skillunit::skill_delunitgroup(c, group_id);
    }
    // Guild bound item check - Removes the bound flag
    for idx in crate::guild_storage::pc_bound_chk(c, aid, crate::inventory::BOUND_GUILD) {
        let amount = crate::inventory::inv_get(c, aid, idx).map(|r| r.item.amount as i32).unwrap_or(0);
        crate::inventory::pc_delitem(c, aid, idx as i32, amount, 0, 1, crate::map_log::LOG_TYPE_BOUND_REMOVAL);
    }
    intif::send(Inter::BreakGuild { guild_id: g.guild.guild_id });
    1
}

// ------------------------------------------------------------------------------------------------ hooks

/// clif_parse_LoadEndAck's guild notice (guild_notice_changemap 2: at every map load, at the end).
pub fn load_end_ack_notice(c: &Ctx, aid: u32) {
    if sd_guild(c, aid).is_some() && guild_notice_changemap() == 2 {
        if let Some(su) = unit::get(c, aid) {
            crate::clif_guild::clif_guild_notice(c, &su);
        }
    }
}

/// clif.cpp:11118-11125 (clif_parse_LoadEndAck, "Guild Aura Init", after mail_clear) and pc.cpp:10157-10162
/// (pc_revive): a guild master's four auras are placed again at their guild's levels.
pub fn guildaura_init(c: &Ctx, aid: u32) {
    let Some(g) = sd_guild(c, aid) else { return };
    if !gx(c, aid).gmaster_flag {
        return;
    }
    for skill_id in GUILD_AURA_SKILLS {
        let lv = guild_checkskill(&g.guild, skill_id as i32);
        guild_guildaura_refresh(c, aid, skill_id, lv.clamp(0, u16::MAX as i32) as u16);
    }
}

/// mmo.hpp GD_LEADERSHIP, GD_GLORYWOUNDS, GD_SOULCOLD, GD_HAWKEYES (10006-10009), in the order clif.cpp:11121-11124
/// and pc.cpp:10158-10161 refresh them.
pub const GUILD_AURA_SKILLS: [u16; 4] = [10006, 10007, 10008, 10009];

/// `case GD_LEADERSHIP: case GD_GLORYWOUNDS: case GD_SOULCOLD: case GD_HAWKEYES:` (guild.cpp:1769-1772, 2341-2344).
pub fn is_guild_aura(skill_id: u16) -> bool {
    GUILD_AURA_SKILLS.contains(&skill_id)
}

/// guild.cpp guild_guildaura_refresh (guild.cpp:1786-1805): the guild master's aura `skill_id` at level `skill_lv`:
/// the old one ends (status_change_end deletes its unit group, status.cpp:13758-13768), a new unit group is placed on
/// the master's cell and the status starts on the master with the group in val4 (the status never ends by itself:
/// status.cpp:14913-14918).
pub fn guild_guildaura_refresh(c: &Ctx, aid: u32, skill_id: u16, skill_lv: u16) {
    let aura = crate::battle_config::get(crate::battle_config::Bc::GUILD_AURA);
    let Some(sd) = unit::get(c, aid) else { return };
    if aura & (if is_agit_start(c) { 2 } else { 1 }) == 0 && aura & (if crate::status::map_flag_gvg2(c, sd.map) { 8 } else { 4 }) == 0 {
        return;
    }
    if skill_lv == 0 {
        return;
    }
    let t = crate::db::skills::skill_get_sc(skill_id);
    if t == crate::db::skills::SC_NONE {
        return;
    }
    let t = t as i32;
    crate::status::sc::status_change_end(c, aid, t, 0);
    // status_change_end may have moved / changed the player's row: read it again
    let Some(sd) = unit::get(c, aid) else { return };
    let group = crate::skillunit::skill_unitsetting(c, &sd, skill_id, skill_lv, sd.x, sd.y, 0);
    if let Some(group_id) = group {
        let Some(sd) = unit::get(c, aid) else { return };
        //duration doesn't matter these status never end with val4
        let val1 = if aura & 16 != 0 { 0 } else { skill_lv as i32 };
        crate::status::sc::sc_start4(c, None, &sd, t, 100, val1, 0, 0, group_id, 600000);
    }
}

/// unit_remove_map's guild part: the invitation and alliance request are refused.
pub fn on_remove_map_invites(c: &Ctx, aid: u32) {
    let x = gx(c, aid);
    if x.guild_invite > 0 {
        guild_reply_invite(c, aid, x.guild_invite, 0);
    }
    let x = gx(c, aid);
    if x.guild_alliance > 0 {
        guild_reply_reqalliance(c, aid, x.guild_alliance_account, 0);
    }
}

/// pc_checkskill for a guild skill: the guild's level of it (0 without a guild).
pub fn pc_checkskill_guild(c: &Ctx, aid: u32, skill_id: u16) -> u8 {
    if guild_id_of(c, aid) > 0 {
        if let Some(g) = sd_guild(c, aid) {
            return guild_checkskill(&g.guild, skill_id as i32).clamp(0, 255) as u8;
        }
    }
    0
}

/// The intif queue's guild messages (intif.rs drain).
pub fn intif_dispatch(c: &Ctx, m: Inter) {
    match m {
        // char server
        Inter::CreateGuild { account_id, name, master } => mapif_parse_create_guild(c, account_id, &name, master),
        Inter::GuildInfo { guild_id } => mapif_parse_guild_info(c, guild_id),
        Inter::GuildAddMember { guild_id, member } => mapif_parse_guild_add_member(c, guild_id, member),
        Inter::GuildMasterChange { guild_id, name, len } => mapif_parse_guild_master_change(c, guild_id, &name, len),
        Inter::GuildLeave { guild_id, account_id, char_id, flag, mes } => mapif_parse_guild_leave(c, guild_id, account_id, char_id, flag, &mes),
        Inter::GuildMemberInfoShort { guild_id, account_id, char_id, online, lv, class } => mapif_parse_guild_change_member_info_short(c, guild_id, account_id, char_id, online, lv, class),
        Inter::BreakGuild { guild_id } => mapif_parse_break_guild(c, guild_id),
        Inter::GuildBasicInfoChange { guild_id, kind, value } => mapif_parse_guild_basic_info_change(c, guild_id, kind, value),
        Inter::GuildMemberInfoChange { guild_id, account_id, char_id, kind, value } => mapif_parse_guild_member_info_change(c, guild_id, account_id, char_id, kind, value),
        Inter::GuildPosition { guild_id, idx, pos } => mapif_parse_guild_position(c, guild_id, idx, pos),
        Inter::GuildSkillUp { guild_id, skill_id, account_id, max } => mapif_parse_guild_skill_up(c, guild_id, skill_id, account_id, max),
        Inter::GuildAlliance { guild_id1, guild_id2, account_id1, account_id2, flag } => {
            mapif_parse_guild_alliance(c, guild_id1, guild_id2, account_id1, account_id2, flag);
        }
        Inter::GuildNotice { guild_id, mes1, mes2 } => mapif_parse_guild_notice(c, guild_id, &mes1, &mes2),
        Inter::GuildEmblem { guild_id, data } => mapif_parse_guild_emblem(c, guild_id, &data),
        Inter::GuildEmblemVersion { guild_id, version } => mapif_parse_guild_emblem_version(c, guild_id, version),
        Inter::GuildCharOnline { char_id } => {
            inter_guild_char_online(c, char_id, -1);
        }
        Inter::GuildCharOffline { char_id } => {
            inter_guild_char_offline(c, char_id, -1);
        }
        Inter::LoadGuildStorage { account_id, guild_id } => crate::guild_storage::mapif_parse_load_guild_storage(c, account_id, guild_id),
        Inter::SaveGuildStorage { account_id, guild_id, items } => crate::guild_storage::mapif_parse_save_guild_storage(c, account_id, guild_id, items),
        // map server
        Inter::GuildCreated { account_id, guild_id } => guild_created(c, account_id, guild_id),
        Inter::GuildInfoAck { guild_id, guild } => match guild {
            Some(g) => guild_recv_info(c, &g),
            None => guild_recv_noinfo(c, guild_id),
        },
        Inter::GuildMemberAdded { guild_id, account_id, char_id, flag } => guild_member_added(c, guild_id, account_id, char_id, flag),
        Inter::GuildWithdraw { guild_id, account_id, char_id, flag, name, mes } => guild_member_withdraw(c, guild_id, account_id, char_id, flag, &name, &mes),
        Inter::GuildMemberInfoShortAck { guild_id, account_id, char_id, online, lv, class } => guild_recv_memberinfoshort(c, guild_id, account_id, char_id, online, lv, class),
        Inter::GuildBroken { guild_id, flag } => {
            guild_broken(c, guild_id, flag);
        }
        Inter::GuildBasicInfoChanged { guild_id, kind, value } => {
            // intif_parse_GuildBasicInfoChanged
            if let Some(mut g) = guild_search(c, guild_id) {
                match kind {
                    GBI_EXP => g.guild.exp = value as u64,
                    GBI_GUILDLV => g.guild.guild_lv = value as i16,
                    GBI_SKILLPOINT => g.guild.skill_point = value as i32,
                    _ => {}
                }
                put_guild(c, &g);
            }
        }
        Inter::GuildMemberInfoChanged { guild_id, account_id, char_id, kind, value } => {
            // intif_parse_GuildMemberInfoChanged
            let Some(mut g) = guild_search(c, guild_id) else { return };
            let Some(idx) = guild_getindex(&g.guild, account_id, char_id) else { return };
            let v = value as i16;
            match kind {
                GMI_POSITION => {
                    g.guild.member[idx].position = v;
                    put_guild(c, &g);
                    guild_memberposition_changed(c, guild_id, idx, v as i32);
                    return;
                }
                GMI_EXP => g.guild.member[idx].exp = value as u64,
                GMI_HAIR => g.guild.member[idx].hair = v,
                GMI_HAIR_COLOR => g.guild.member[idx].hair_color = v,
                GMI_GENDER => g.guild.member[idx].gender = v,
                GMI_CLASS => g.guild.member[idx].class = v,
                GMI_LEVEL => g.guild.member[idx].lv = v,
                _ => {}
            }
            put_guild(c, &g);
        }
        Inter::GuildPositionAck { guild_id, idx, pos } => guild_position_changed(c, guild_id, idx, &pos),
        Inter::GuildSkillUpAck { guild_id, skill_id, account_id } => guild_skillupack(c, guild_id, skill_id, account_id),
        Inter::GuildAllianceAck { guild_id1, guild_id2, account_id1, account_id2, flag, name1, name2 } => guild_allianceack(c, guild_id1, guild_id2, account_id1, account_id2, flag as i32, &name1, &name2),
        Inter::GuildNoticeAck { guild_id, mes1, mes2 } => guild_notice_changed(c, guild_id, &mes1, &mes2),
        Inter::GuildEmblemAck { guild_id, emblem_id, data } => guild_emblem_changed(c, guild_id, emblem_id, Some(&data)),
        Inter::GuildEmblemVersionAck { guild_id, emblem_id } => guild_emblem_changed(c, guild_id, emblem_id, None),
        Inter::GuildMasterChanged { guild_id, account_id, char_id, time } => {
            guild_gm_changed(c, guild_id, account_id, char_id, time);
        }
        Inter::LoadGuildStorageAck { account_id, guild_id, flag, storage } => crate::guild_storage::intif_parse_load_guild_storage(c, account_id, guild_id, flag, storage),
        Inter::SaveGuildStorageAck { guild_id, .. } => crate::guild_storage::storage_guild_storagesaved(c, guild_id),
        _ => {}
    }
}
