//! char/int_guild.cpp: the char server's side of guilds — the SQL tables (`guild`, `guild_member`, `guild_position`,
//! `guild_alliance`, `guild_expulsion`, `guild_skill`), the char server's guild_db cache (CharGuild: the mmo_guild and
//! its save_flag), guild_save_timer (the lazy save and the unloading of guilds nobody uses), exp_guild.yml
//! (GuildExpDatabase) and the inter-server requests of the map server (intif.rs queues them; the answers go back
//! through the same queue to guild.rs).
//!
//! Two copies of every loaded guild exist, as in rAthena: this one (the char server's cache, saved to the SQL tables
//! by inter_guild_tosql) and the map server's guild_db (guild.rs `guild_db`), which changes only through the answers
//! sent from here.
//!
//! `char`.`guild_id` writes: rAthena's char server updates the SQL column while the map server keeps its own copy in
//! sd->status.guild_id and writes that back whenever it saves the character. The module has one `character` row, so
//! these writes are applied to characters that are not online; for an online character the map server's value
//! (guild.rs sets it when it handles the answer) is the one rAthena would end up saving (same rule as int_party.rs).
//!
//! `char`.`online` (read when a guild is loaded from SQL): a character is online for the char server between
//! char_set_char_online and char_set_char_offline; the module reads it as "the character is in the world" (a `pc`
//! row), plus the character being set online itself (inter_guild_CharOnline marks that member explicitly).

use spacetimedb::{table, SpacetimeType, Table};

use crate::character::character;
use crate::intif::{self, Inter};
use crate::pc::pc as _;
use crate::world::Ctx;

// ------------------------------------------------------------------------------------------------ mmo.hpp

/// mmo.hpp MAX_GUILD (16 + 10 * 6).
pub const MAX_GUILD: usize = 16 + 10 * 6;
/// mmo.hpp MAX_GUILDPOSITION.
pub const MAX_GUILDPOSITION: usize = 20;
/// mmo.hpp MAX_GUILDEXPULSION.
pub const MAX_GUILDEXPULSION: usize = 32;
/// mmo.hpp MAX_GUILDALLIANCE.
pub const MAX_GUILDALLIANCE: usize = 16;
/// mmo.hpp MAX_GUILDSKILL (RENEWAL).
pub const MAX_GUILDSKILL: usize = 20;
/// mmo.hpp MAX_GUILDLEVEL.
pub const MAX_GUILDLEVEL: i16 = 50;
/// mmo.hpp MAX_GUILDMES1 / MAX_GUILDMES2.
pub const MAX_GUILDMES1: usize = 60;
pub const MAX_GUILDMES2: usize = 120;
/// config/const.hpp MAX_GUILD_EXP = INT32_MAX.
pub const MAX_GUILD_EXP: u64 = i32::MAX as u64;
/// mmo.hpp NAME_LENGTH.
pub const NAME_LENGTH: usize = 24;
/// mmo.hpp e_guild_skill GD_SKILLBASE.
pub const GD_SKILLBASE: i32 = 10000;
/// mmo.hpp GD_EXTENSION / GD_GUILD_STORAGE.
pub const GD_EXTENSION: i32 = 10004;
pub const GD_GUILD_STORAGE: i32 = 10016;
/// mmo.hpp sizeof(mmo_guild::emblem_data).
pub const EMBLEM_DATA_SIZE: usize = 2048;

/// mmo.hpp e_guild_permission (PACKETVER >= 20140205: GUILD_PERM_ALL includes the storage).
pub const GUILD_PERM_INVITE: i32 = 0x001;
pub const GUILD_PERM_EXPEL: i32 = 0x010;
pub const GUILD_PERM_STORAGE: i32 = 0x100;
pub const GUILD_PERM_ALL: i32 = GUILD_PERM_INVITE | GUILD_PERM_EXPEL | GUILD_PERM_STORAGE;
pub const GUILD_PERM_DEFAULT: i32 = GUILD_PERM_ALL;

/// mmo.hpp e_guild_info.
pub const GBI_EXP: u16 = 1;
pub const GBI_GUILDLV: u16 = 2;
pub const GBI_SKILLPOINT: u16 = 3;
pub const GBI_SKILLLV: u16 = 4;

/// mmo.hpp e_guild_member_info.
pub const GMI_POSITION: u16 = 0;
pub const GMI_EXP: u16 = 1;
pub const GMI_HAIR: u16 = 2;
pub const GMI_HAIR_COLOR: u16 = 3;
pub const GMI_GENDER: u16 = 4;
pub const GMI_CLASS: u16 = 5;
pub const GMI_LEVEL: u16 = 6;

/// mmo.hpp struct guild_member. `sd` is the map server's map_session_data pointer (the account id of the online
/// member, 0 = none); the char server never reads it.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct GuildMember {
    pub account_id: u32,
    pub char_id: u32,
    pub hair: i16,
    pub hair_color: i16,
    pub gender: i16,
    pub class: i16,
    pub lv: i16,
    pub exp: u64,
    pub online: i16,
    pub position: i16,
    pub name: String,
    pub sd: u32,
    pub modified: u8,
    pub last_login: u32,
}

/// mmo.hpp struct guild_position.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct GuildPosition {
    pub name: String,
    pub mode: i32,
    pub exp_mode: i32,
    pub modified: u8,
}

/// mmo.hpp struct guild_alliance.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct GuildAlliance {
    pub opposition: i32,
    pub guild_id: i32,
    pub name: String,
}

/// mmo.hpp struct guild_expulsion.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct GuildExpulsion {
    pub name: String,
    /// char mes[40].
    pub mes: String,
    pub account_id: u32,
    pub char_id: u32,
}

/// mmo.hpp struct guild_skill.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct GuildSkill {
    pub id: i32,
    pub lv: i32,
}

/// mmo.hpp struct mmo_guild. The arrays always have their full length (MAX_GUILD members, MAX_GUILDPOSITION
/// positions, MAX_GUILDALLIANCE alliances, MAX_GUILDEXPULSION expulsions, MAX_GUILDSKILL skills); `emblem_data` holds
/// the first emblem_len bytes.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct MmoGuild {
    pub guild_id: i32,
    pub guild_lv: i16,
    pub connect_member: i16,
    pub max_member: i16,
    pub average_lv: i16,
    pub exp: u64,
    pub next_exp: u64,
    pub skill_point: i32,
    pub name: String,
    pub master: String,
    pub member: Vec<GuildMember>,
    pub position: Vec<GuildPosition>,
    /// char mes1[MAX_GUILDMES1], mes2[MAX_GUILDMES2] (as bytes read up to the first NUL).
    pub mes1: String,
    pub mes2: String,
    pub emblem_len: i32,
    pub emblem_id: i32,
    pub emblem_data: Vec<u8>,
    pub alliance: Vec<GuildAlliance>,
    pub expulsion: Vec<GuildExpulsion>,
    pub skill: Vec<GuildSkill>,
    /// time_t last_leader_change (seconds).
    pub last_leader_change: i64,
}

impl MmoGuild {
    /// A zeroed mmo_guild (`= {}` / memset 0).
    pub fn empty() -> MmoGuild {
        MmoGuild {
            member: vec![GuildMember::default(); MAX_GUILD],
            position: vec![GuildPosition::default(); MAX_GUILDPOSITION],
            alliance: vec![GuildAlliance::default(); MAX_GUILDALLIANCE],
            expulsion: vec![GuildExpulsion::default(); MAX_GUILDEXPULSION],
            skill: vec![GuildSkill::default(); MAX_GUILDSKILL],
            ..Default::default()
        }
    }
    /// g.max_member as an index bound (never above MAX_GUILD, never negative).
    pub fn max(&self) -> usize {
        (self.max_member.max(0) as usize).min(MAX_GUILD)
    }
}

/// safestrncpy(dst, src, n) of a latin-1 string: at most n - 1 bytes, cut at the first NUL.
pub fn trunc(s: &str, n: usize) -> String {
    let b: Vec<u8> = crate::net::wire::latin1(s).into_iter().take_while(|&x| x != 0).take(n.saturating_sub(1)).collect();
    crate::net::wire::from_latin1(&b)
}

/// strncmp(a, b, n) == 0 for two latin-1 strings (bytes up to the first NUL).
pub fn strncmp_eq(a: &str, b: &str, n: usize) -> bool {
    let a: Vec<u8> = crate::net::wire::latin1(a).into_iter().take_while(|&x| x != 0).take(n).collect();
    let b: Vec<u8> = crate::net::wire::latin1(b).into_iter().take_while(|&x| x != 0).take(n).collect();
    a == b
}

// ------------------------------------------------------------------------------------------------ SQL tables

/// rAthena `guild` table (sql-files/main.sql). `emblem_data` keeps the bytes the char server writes as hex text.
/// `last_master_change` is FROM_UNIXTIME(last_leader_change) (0 = NULL).
#[table(accessor = guild_sql)]
#[derive(Clone, Debug)]
pub struct GuildSql {
    #[primary_key]
    pub guild_id: u32,
    pub name: String,
    pub char_id: u32,
    pub master: String,
    pub guild_lv: u8,
    pub connect_member: u8,
    pub max_member: u8,
    pub average_lv: u16,
    pub exp: u64,
    pub next_exp: u64,
    pub skill_point: u8,
    pub mes1: String,
    pub mes2: String,
    pub emblem_len: u32,
    pub emblem_id: u32,
    pub emblem_data: Vec<u8>,
    pub last_master_change: i64,
}

/// rAthena `guild_member` table (PRIMARY KEY guild_id, char_id).
#[table(accessor = guild_member_sql, index(accessor = by_guild, btree(columns = [guild_id])))]
#[derive(Clone, Debug)]
pub struct GuildMemberSql {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub guild_id: u32,
    #[index(btree)]
    pub char_id: u32,
    pub exp: u64,
    pub position: u8,
}

/// rAthena `guild_position` table (PRIMARY KEY guild_id, position).
#[table(accessor = guild_position_sql, index(accessor = by_guild, btree(columns = [guild_id])))]
#[derive(Clone, Debug)]
pub struct GuildPositionSql {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub guild_id: u32,
    pub position: u8,
    pub name: String,
    pub mode: u16,
    pub exp_mode: u8,
}

/// rAthena `guild_alliance` table (PRIMARY KEY guild_id, alliance_id).
#[table(accessor = guild_alliance_sql, index(accessor = by_guild, btree(columns = [guild_id])))]
#[derive(Clone, Debug)]
pub struct GuildAllianceSql {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub guild_id: u32,
    pub opposition: u32,
    #[index(btree)]
    pub alliance_id: u32,
    pub name: String,
}

/// rAthena `guild_expulsion` table (PRIMARY KEY guild_id, name).
#[table(accessor = guild_expulsion_sql, index(accessor = by_guild, btree(columns = [guild_id])))]
#[derive(Clone, Debug)]
pub struct GuildExpulsionSql {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub guild_id: u32,
    pub account_id: u32,
    pub name: String,
    pub mes: String,
    pub char_id: u32,
}

/// rAthena `guild_skill` table (PRIMARY KEY guild_id, id).
#[table(accessor = guild_skill_sql, index(accessor = by_guild, btree(columns = [guild_id])))]
#[derive(Clone, Debug)]
pub struct GuildSkillSql {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub guild_id: u32,
    pub skill_id: u16,
    pub lv: u8,
}

// ------------------------------------------------------------------------------------------------ the cache

/// int_guild.hpp e_guild_action (the save_flag bits of CharGuild).
pub const GS_BASIC: u16 = 0x0001;
pub const GS_MEMBER: u16 = 0x0002;
pub const GS_POSITION: u16 = 0x0004;
pub const GS_ALLIANCE: u16 = 0x0008;
pub const GS_EXPULSION: u16 = 0x0010;
pub const GS_SKILL: u16 = 0x0020;
pub const GS_EMBLEM: u16 = 0x0040;
pub const GS_CONNECT: u16 = 0x0080;
pub const GS_LEVEL: u16 = 0x0100;
pub const GS_MES: u16 = 0x0200;
pub const GS_MASK: u16 = 0x03FF;
pub const GS_BASIC_MASK: u16 = GS_BASIC | GS_EMBLEM | GS_CONNECT | GS_LEVEL | GS_MES;
pub const GS_REMOVE: u16 = 0x8000;

/// int_guild.cpp GS_MEMBER_* / GS_POSITION_* (guild_member::modified, guild_position::modified).
pub const GS_MEMBER_UNMODIFIED: u8 = 0x00;
pub const GS_MEMBER_MODIFIED: u8 = 0x01;
pub const GS_MEMBER_NEW: u8 = 0x02;
pub const GS_POSITION_UNMODIFIED: u8 = 0x00;
pub const GS_POSITION_MODIFIED: u8 = 0x01;

/// int_guild.cpp GUILD_ALLIANCE_TYPE_MASK / GUILD_ALLIANCE_REMOVE.
pub const GUILD_ALLIANCE_TYPE_MASK: i32 = 0x01;
pub const GUILD_ALLIANCE_REMOVE: i32 = 0x08;

/// int_guild.hpp class CharGuild: the char server's guild_db (loaded from SQL on first use).
#[table(accessor = inter_guild)]
#[derive(Clone, Debug)]
pub struct CharGuild {
    #[primary_key]
    pub guild_id: u32,
    pub guild: MmoGuild,
    pub save_flag: u16,
}

/// int_guild.cpp's statics: guild_save_timer's `last_id` and the timer itself.
#[table(accessor = inter_guild_state)]
pub struct InterGuildState {
    #[primary_key]
    pub key: u8,
    pub save_timer: u64,
    pub last_id: i32,
}

/// conf/char_athena.conf `autosave_time: 60` (charserv_config.autosave_interval = 60 * 1000).
pub const AUTOSAVE_INTERVAL: i64 = 60 * 1000;
/// conf/char_athena.conf `guild_exp_rate: 100`.
pub const GUILD_EXP_RATE: u64 = 100;
/// conf/char_athena.conf `char_name_option: 1` (only the letters of char_name_letters).
const CHAR_NAME_OPTION: i32 = 1;

// ------------------------------------------------------------------------------------------------ exp_guild.yml

#[derive(serde::Deserialize)]
pub struct GuildExpFile {
    /// (level, exp) in level order.
    pub exp: Vec<(u16, u64)>,
}
crate::dataset!(guild_exp_db, "guild_exp", GuildExpFile);

/// int_guild.cpp GuildExpDatabase::get_nextexp (0 when the level has no entry).
pub fn get_nextexp(level: i16) -> u64 {
    if level < 0 {
        return 0;
    }
    guild_exp_db().exp.iter().find(|(l, _)| *l as i16 == level).map(|(_, e)| *e).unwrap_or(0)
}

// ------------------------------------------------------------------------------------------------ helpers

fn state(c: &Ctx) -> InterGuildState {
    c.db().inter_guild_state().key().find(0).unwrap_or(InterGuildState { key: 0, save_timer: 0, last_id: 0 })
}

fn put_state(c: &Ctx, s: InterGuildState) {
    if c.db().inter_guild_state().key().find(0).is_some() {
        c.db().inter_guild_state().key().update(s);
    } else {
        c.db().inter_guild_state().insert(s);
    }
}

/// inter_guild_sql_init: add_timer(gettick() + 10000, guild_save_timer). The module arms it the first time the char
/// server touches a guild (and again if the row is ever gone).
pub fn ensure_timer(c: &Ctx) {
    let mut s = state(c);
    if s.save_timer != 0 && crate::timer::get(c, s.save_timer).is_some() {
        return;
    }
    s.save_timer = crate::timer::add(c, c.tick() + 10_000, crate::timer::Func::GuildSaveTimer, 0, 0);
    put_state(c, s);
}

fn get_cache(c: &Ctx, guild_id: i32) -> Option<CharGuild> {
    if guild_id <= 0 {
        return None;
    }
    c.db().inter_guild().guild_id().find(guild_id as u32)
}

/// Writes a CharGuild back to guild_db (the shared_ptr's object in rAthena).
pub fn put_cache(c: &Ctx, g: &CharGuild) {
    if c.db().inter_guild().guild_id().find(g.guild_id).is_some() {
        c.db().inter_guild().guild_id().update(g.clone());
    } else {
        c.db().inter_guild().insert(g.clone());
    }
}

fn char_online(c: &Ctx, char_id: u32) -> bool {
    c.db().pc().char_id().find(char_id).is_some()
}

/// `UPDATE char SET guild_id = … WHERE …` for one character (see the module doc: offline characters only).
fn sql_set_char_guild(c: &Ctx, char_id: u32, account_id: Option<u32>, only_if_guild: Option<u32>, guild_id: u32) {
    let Some(mut ch) = c.db().character().id().find(char_id) else { return };
    if account_id.is_some_and(|a| a != ch.account_id) || only_if_guild.is_some_and(|g| g != ch.guild_id) {
        return;
    }
    if char_online(c, char_id) || ch.guild_id == guild_id {
        return;
    }
    ch.guild_id = guild_id;
    c.db().character().id().update(ch);
}

/// MySQL's comparison of a varchar(24) against a value under the default (case-insensitive, PAD SPACE) collation.
fn sql_name_eq(a: &str, b: &str) -> bool {
    let norm = |s: &str| -> Vec<u8> {
        let mut v: Vec<u8> = crate::net::wire::latin1(s).into_iter().take_while(|&x| x != 0).collect();
        while v.last() == Some(&b' ') {
            v.pop();
        }
        v
    };
    norm(a).eq_ignore_ascii_case(&norm(b))
}

// ------------------------------------------------------------------------------------------------ tosql / fromsql

/// int_guild.cpp inter_guild_removemember_tosql.
fn inter_guild_removemember_tosql(c: &Ctx, char_id: u32) {
    let rows: Vec<u64> = c.db().guild_member_sql().char_id().filter(char_id).map(|r| r.id).collect();
    for id in rows {
        c.db().guild_member_sql().id().delete(id);
    }
    sql_set_char_guild(c, char_id, None, None, 0);
}

/// int_guild.cpp inter_guild_tosql: saves the parts of the guild named by `flag`. A guild with guild_id -1 is
/// created (GS_BASIC) and gets its new id. Returns false when nothing could be saved.
pub fn inter_guild_tosql(c: &Ctx, g: &mut MmoGuild, flag: u16) -> bool {
    if g.guild_id <= 0 && g.guild_id != -1 {
        return false;
    }
    let mut new_guild = false;
    // Insert a new guild the guild
    if flag & GS_BASIC != 0 && g.guild_id == -1 {
        // INSERT INTO guild (name, master, guild_lv, max_member, average_lv, char_id) (auto_increment guild_id)
        let id = crate::ids::next(c.rc, "guild", 1);
        c.db().guild_sql().insert(GuildSql {
            guild_id: id,
            name: trunc(&g.name, NAME_LENGTH + 1),
            char_id: g.member[0].char_id,
            master: trunc(&g.master, NAME_LENGTH + 1),
            guild_lv: g.guild_lv.clamp(0, 255) as u8,
            connect_member: 0,
            max_member: g.max_member.clamp(0, 255) as u8,
            average_lv: g.average_lv.max(0) as u16,
            exp: 0,
            next_exp: 0,
            skill_point: 0,
            mes1: String::new(),
            mes2: String::new(),
            emblem_len: 0,
            emblem_id: 0,
            emblem_data: Vec::new(),
            last_master_change: 0,
        });
        g.guild_id = id as i32;
        new_guild = true;
    }
    let gid = g.guild_id as u32;
    // If we need an update on an existing guild or more update on the new guild
    if (flag & GS_BASIC_MASK != 0 && !new_guild) || (flag & (GS_BASIC_MASK & !GS_BASIC) != 0 && new_guild) {
        if let Some(mut row) = c.db().guild_sql().guild_id().find(gid) {
            if flag & GS_EMBLEM != 0 {
                row.emblem_len = g.emblem_len.max(0) as u32;
                row.emblem_id = g.emblem_id as u32;
                row.emblem_data = g.emblem_data.iter().copied().take(g.emblem_len.max(0) as usize).collect();
            }
            if flag & GS_BASIC != 0 {
                row.name = trunc(&g.name, NAME_LENGTH + 1);
                row.master = trunc(&g.master, NAME_LENGTH + 1);
                row.char_id = g.member[0].char_id;
                if g.last_leader_change != 0 {
                    row.last_master_change = g.last_leader_change;
                }
            }
            if flag & GS_CONNECT != 0 {
                row.connect_member = g.connect_member.clamp(0, 255) as u8;
                row.average_lv = g.average_lv.max(0) as u16;
            }
            if flag & GS_MES != 0 {
                row.mes1 = trunc(&g.mes1, MAX_GUILDMES1 + 1);
                row.mes2 = trunc(&g.mes2, MAX_GUILDMES2 + 1);
            }
            if flag & GS_LEVEL != 0 {
                row.guild_lv = g.guild_lv.clamp(0, 255) as u8;
                row.skill_point = g.skill_point.clamp(0, 255) as u8;
                row.exp = g.exp;
                row.next_exp = g.next_exp;
                row.max_member = g.max_member.clamp(0, 255) as u8;
            }
            c.db().guild_sql().guild_id().update(row);
        }
    }
    if flag & GS_MEMBER != 0 {
        // Update only needed players
        for i in 0..g.max() {
            let m = g.member[i].clone();
            if m.modified == 0 {
                continue;
            }
            if m.account_id != 0 {
                // REPLACE INTO guild_member (guild_id, char_id, exp, position)
                let existing: Vec<GuildMemberSql> = c.db().guild_member_sql().by_guild().filter(gid).filter(|r| r.char_id == m.char_id).collect();
                let pos = m.position.clamp(0, 255) as u8;
                match existing.first() {
                    Some(r) => {
                        c.db().guild_member_sql().id().update(GuildMemberSql { exp: m.exp, position: pos, ..r.clone() });
                    }
                    None => {
                        c.db().guild_member_sql().insert(GuildMemberSql { id: 0, guild_id: gid, char_id: m.char_id, exp: m.exp, position: pos });
                    }
                }
                if m.modified & GS_MEMBER_NEW != 0 || new_guild {
                    // UPDATE char SET guild_id WHERE char_id
                    sql_set_char_guild(c, m.char_id, None, None, gid);
                }
                g.member[i].modified = GS_MEMBER_UNMODIFIED;
            }
        }
    }
    if flag & GS_POSITION != 0 {
        for i in 0..MAX_GUILDPOSITION {
            if g.position[i].modified == 0 {
                continue;
            }
            let p = g.position[i].clone();
            // REPLACE INTO guild_position (guild_id, position, name, mode, exp_mode)
            let existing: Vec<GuildPositionSql> = c.db().guild_position_sql().by_guild().filter(gid).filter(|r| r.position as usize == i).collect();
            let row = GuildPositionSql { id: 0, guild_id: gid, position: i as u8, name: trunc(&p.name, NAME_LENGTH + 1), mode: p.mode.clamp(0, 65535) as u16, exp_mode: p.exp_mode.clamp(0, 255) as u8 };
            match existing.first() {
                Some(r) => c.db().guild_position_sql().id().update(GuildPositionSql { id: r.id, ..row }),
                None => c.db().guild_position_sql().insert(row),
            };
            g.position[i].modified = GS_POSITION_UNMODIFIED;
        }
    }
    if flag & GS_ALLIANCE != 0 {
        // Delete current alliances (only this guild's side: the other guild in memory saves its own)
        let rows: Vec<u64> = c.db().guild_alliance_sql().by_guild().filter(gid).map(|r| r.id).collect();
        for id in rows {
            c.db().guild_alliance_sql().id().delete(id);
        }
        for a in g.alliance.iter() {
            if a.guild_id > 0 {
                // REPLACE INTO guild_alliance (guild_id, opposition, alliance_id, name)
                let existing: Vec<GuildAllianceSql> = c.db().guild_alliance_sql().by_guild().filter(gid).filter(|r| r.alliance_id == a.guild_id as u32).collect();
                let row = GuildAllianceSql { id: 0, guild_id: gid, opposition: a.opposition as u32, alliance_id: a.guild_id as u32, name: trunc(&a.name, NAME_LENGTH + 1) };
                match existing.first() {
                    Some(r) => c.db().guild_alliance_sql().id().update(GuildAllianceSql { id: r.id, ..row }),
                    None => c.db().guild_alliance_sql().insert(row),
                };
            }
        }
    }
    if flag & GS_EXPULSION != 0 {
        for e in g.expulsion.iter() {
            if e.account_id > 0 {
                // REPLACE INTO guild_expulsion (guild_id, account_id, name, mes, char_id): the key is (guild_id, name)
                let name = trunc(&e.name, NAME_LENGTH + 1);
                let existing: Vec<GuildExpulsionSql> = c.db().guild_expulsion_sql().by_guild().filter(gid).filter(|r| sql_name_eq(&r.name, &name)).collect();
                let row = GuildExpulsionSql { id: 0, guild_id: gid, account_id: e.account_id, name, mes: trunc(&e.mes, 41), char_id: e.char_id };
                match existing.first() {
                    Some(r) => c.db().guild_expulsion_sql().id().update(GuildExpulsionSql { id: r.id, ..row }),
                    None => c.db().guild_expulsion_sql().insert(row),
                };
            }
        }
    }
    if flag & GS_SKILL != 0 {
        for s in g.skill.iter() {
            if s.id > 0 && s.lv > 0 {
                // REPLACE INTO guild_skill (guild_id, id, lv)
                let existing: Vec<GuildSkillSql> = c.db().guild_skill_sql().by_guild().filter(gid).filter(|r| r.skill_id as i32 == s.id).collect();
                let row = GuildSkillSql { id: 0, guild_id: gid, skill_id: s.id.clamp(0, 65535) as u16, lv: s.lv.clamp(0, 255) as u8 };
                match existing.first() {
                    Some(r) => c.db().guild_skill_sql().id().update(GuildSkillSql { id: r.id, ..row }),
                    None => c.db().guild_skill_sql().insert(row),
                };
            }
        }
    }
    // (charserv_config.save_log: "Saved guild (%d - %s):%s")
    log::info!("Saved guild ({} - {}) flag 0x{flag:x}", g.guild_id, g.name);
    true
}

/// int_guild.cpp inter_guild_fromsql: the guild from the cache, or loaded from SQL into it (marked GS_REMOVE, "in case
/// it is not needed for long").
///
/// Row orders the SQL leaves to the server: the members come `ORDER BY position` (ties in char id order, the
/// guild_member primary key the MyISAM `ref` read follows); positions are placed by their index; alliances and
/// expulsions come in their primary key order (alliance id / name); skills `ORDER BY id`.
pub fn inter_guild_fromsql(c: &Ctx, guild_id: i32) -> Option<CharGuild> {
    if guild_id <= 0 {
        return None;
    }
    if let Some(g) = get_cache(c, guild_id) {
        return Some(g);
    }
    let gid = guild_id as u32;
    let row = c.db().guild_sql().guild_id().find(gid)?;
    let mut g = MmoGuild::empty();
    g.guild_id = guild_id;
    g.name = trunc(&row.name, NAME_LENGTH + 1);
    // c.`name` of LEFT JOIN char c ON c.char_id = g.char_id (a missing character leaves the master empty)
    g.master = c.db().character().id().find(row.char_id).map(|ch| trunc(&ch.name, NAME_LENGTH + 1)).unwrap_or_default();
    g.guild_lv = row.guild_lv as i16;
    g.connect_member = row.connect_member as i16;
    g.max_member = row.max_member as i16;
    if g.max_member as usize > MAX_GUILD {
        // Fix reduction of MAX_GUILD [PoW]
        log::warn!("Guild {}:{} specifies higher capacity ({}) than MAX_GUILD ({})", guild_id, g.name, g.max_member, MAX_GUILD);
        g.max_member = MAX_GUILD as i16;
    }
    g.average_lv = row.average_lv as i16;
    g.exp = row.exp;
    g.next_exp = row.next_exp;
    g.skill_point = row.skill_point as i32;
    g.mes1 = row.mes1.clone();
    g.mes2 = row.mes2.clone();
    g.emblem_len = row.emblem_len as i32;
    g.emblem_id = row.emblem_id as i32;
    g.last_leader_change = row.last_master_change;
    // convert emblem data from hexadecimal to binary (the module keeps the bytes)
    g.emblem_data = row.emblem_data.iter().copied().take(g.emblem_len.max(0) as usize).collect();
    g.emblem_data.resize(g.emblem_len.max(0) as usize, 0);
    // load guild member info: guild_member m INNER JOIN char c ... ORDER BY position
    let mut members: Vec<(GuildMemberSql, crate::character::Character)> = c
        .db()
        .guild_member_sql()
        .by_guild()
        .filter(gid)
        .filter_map(|m| c.db().character().id().find(m.char_id).map(|ch| (m, ch)))
        .collect();
    members.sort_by_key(|(m, _)| (m.position, m.char_id));
    for (i, (m, ch)) in members.into_iter().take(g.max()).enumerate() {
        let mut position = m.position as i16;
        if position as usize >= MAX_GUILDPOSITION {
            // Fix reduction of MAX_GUILDPOSITION [PoW]
            position = MAX_GUILDPOSITION as i16 - 1;
        }
        g.member[i] = GuildMember {
            account_id: ch.account_id,
            char_id: m.char_id,
            hair: ch.hair as i16,
            hair_color: ch.hair_color as i16,
            gender: ch.sex as i16,
            class: ch.class as i16,
            lv: ch.base_level as i16,
            exp: m.exp,
            online: char_online(c, m.char_id) as i16,
            position,
            name: trunc(&ch.name, NAME_LENGTH + 1),
            sd: 0,
            modified: GS_MEMBER_UNMODIFIED,
            last_login: ch.last_login.map(|t| (t.to_micros_since_unix_epoch() / 1_000_000) as u32).unwrap_or(0),
        };
    }
    for p in c.db().guild_position_sql().by_guild().filter(gid) {
        let idx = p.position as usize;
        if idx >= MAX_GUILDPOSITION {
            continue; // invalid position
        }
        g.position[idx] = GuildPosition { name: trunc(&p.name, NAME_LENGTH + 1), mode: p.mode as i32, exp_mode: p.exp_mode as i32, modified: GS_POSITION_UNMODIFIED };
    }
    let mut alliances: Vec<GuildAllianceSql> = c.db().guild_alliance_sql().by_guild().filter(gid).collect();
    alliances.sort_by_key(|a| a.alliance_id);
    for (i, a) in alliances.into_iter().take(MAX_GUILDALLIANCE).enumerate() {
        g.alliance[i] = GuildAlliance { opposition: a.opposition as i32, guild_id: a.alliance_id as i32, name: trunc(&a.name, NAME_LENGTH + 1) };
    }
    let mut expulsions: Vec<GuildExpulsionSql> = c.db().guild_expulsion_sql().by_guild().filter(gid).collect();
    expulsions.sort_by(|a, b| crate::net::wire::latin1(&a.name).to_ascii_lowercase().cmp(&crate::net::wire::latin1(&b.name).to_ascii_lowercase()));
    for (i, e) in expulsions.into_iter().take(MAX_GUILDEXPULSION).enumerate() {
        g.expulsion[i] = GuildExpulsion { account_id: e.account_id, name: trunc(&e.name, NAME_LENGTH + 1), mes: trunc(&e.mes, 41), char_id: e.char_id };
    }
    // Skill IDs must always be initialized. [Skotlex]
    for i in 0..MAX_GUILDSKILL {
        g.skill[i].id = i as i32 + GD_SKILLBASE;
    }
    for s in c.db().guild_skill_sql().by_guild().filter(gid) {
        let id = s.skill_id as i32 - GD_SKILLBASE;
        if id < 0 || id as usize >= MAX_GUILDSKILL {
            continue; // invalid guild skill
        }
        g.skill[id as usize].lv = s.lv as i32;
    }
    // Add to cache, but set it to be removed, in case it is not needed for long.
    let cg = CharGuild { guild_id: gid, guild: g, save_flag: GS_REMOVE };
    put_cache(c, &cg);
    ensure_timer(c);
    log::info!("Guild loaded ({} - {})", guild_id, cg.guild.name);
    Some(cg)
}

/// int_guild.cpp guild_save_timer: saves the next guild with pending changes after the last one saved, unloads the
/// guilds whose only flag is GS_REMOVE, and re-arms itself for autosave_interval / number of guilds.
/// The cache is walked in guild id order (rAthena walks its unordered_map; which guild saves first is not
/// observable by a client).
pub fn guild_save_timer(c: &Ctx, tick: crate::world::Tick) {
    let mut s = state(c);
    let mut last_id = s.last_id;
    let mut st = 0; // 0: have not reached last guild. 1: reached last guild, ready for save. 2: some guild saved.
    if last_id == 0 {
        st = 1; // Save the first guild in the list.
    }
    let mut ids: Vec<u32> = c.db().inter_guild().iter().map(|g| g.guild_id).collect();
    ids.sort_unstable();
    for id in ids {
        let Some(mut g) = c.db().inter_guild().guild_id().find(id) else { continue };
        if st == 0 && g.guild.guild_id == last_id {
            st += 1; // Save next guild in the list.
        } else if st == 1 && g.save_flag & GS_MASK != 0 {
            let flag = g.save_flag & GS_MASK;
            inter_guild_tosql(c, &mut g.guild, flag);
            g.save_flag &= !GS_MASK;
            // Some guild saved.
            last_id = g.guild.guild_id;
            st += 1;
            put_cache(c, &g);
        }
        // Nothing to save, guild is ready for removal.
        if g.save_flag == GS_REMOVE {
            log::info!("Guild Unloaded ({} - {})", g.guild.guild_id, g.guild.name);
            c.db().inter_guild().guild_id().delete(id);
        }
    }
    if st != 2 {
        last_id = 0; // Reached the end of the guild db without saving.
    }
    let count = (c.db().inter_guild().count() as i64).max(1);
    // Calculate the time slot for the next save.
    s.last_id = last_id;
    s.save_timer = crate::timer::add(c, tick + AUTOSAVE_INTERVAL / count, crate::timer::Func::GuildSaveTimer, 0, 0);
    put_state(c, s);
}

/// int_guild.cpp inter_guild_storagemax (OFFICIAL_GUILD_STORAGE, PACKETVER >= 20131223): 100 + GD_GUILD_STORAGE
/// level * 100, 0 without the skill.
pub fn inter_guild_storagemax(c: &Ctx, guild_id: i32) -> u16 {
    let Some(g) = inter_guild_fromsql(c, guild_id) else {
        log::error!("Guild {guild_id} not found!");
        return 0;
    };
    let mut max = guild_checkskill(&g.guild, GD_GUILD_STORAGE) as u16;
    if max != 0 {
        max = 100 + max * 100; // Level 1..5 => 200..600
    }
    max
}

/// int_guild.cpp inter_guild_CharOnline (char.cpp char_set_char_online): the member is online for the char server.
pub fn inter_guild_char_online(c: &Ctx, char_id: u32, guild_id: i32) -> i32 {
    let mut guild_id = guild_id;
    if guild_id == -1 {
        // Get guild_id from the database
        guild_id = c.db().character().id().find(char_id).map(|ch| ch.guild_id as i32).unwrap_or(0);
    }
    if guild_id == 0 {
        return 0; // No guild...
    }
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else {
        log::error!("Character {char_id}'s guild {guild_id} not found!");
        return 0;
    };
    // Member has logged in before saving, tell saver not to delete
    if g.save_flag & GS_REMOVE != 0 {
        g.save_flag &= !GS_REMOVE;
    }
    // Set member online
    let max = g.guild.max();
    if let Some(i) = (0..max).find(|&i| g.guild.member[i].char_id == char_id) {
        g.guild.member[i].online = 1;
        g.guild.member[i].modified = GS_MEMBER_MODIFIED;
    }
    put_cache(c, &g);
    1
}

/// int_guild.cpp inter_guild_CharOffline (char.cpp char_set_char_offline).
pub fn inter_guild_char_offline(c: &Ctx, char_id: u32, guild_id: i32) -> i32 {
    let mut guild_id = guild_id;
    if guild_id == -1 {
        guild_id = c.db().character().id().find(char_id).map(|ch| ch.guild_id as i32).unwrap_or(0);
    }
    if guild_id == 0 {
        return 0; // No guild...
    }
    // Character has a guild, set character offline and check if they were the only member online
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else { return 0 };
    let max = g.guild.max();
    if let Some(i) = (0..max).find(|&i| g.guild.member[i].char_id == char_id) {
        g.guild.member[i].online = 0;
        g.guild.member[i].modified = GS_MEMBER_MODIFIED;
    }
    let online_count = (0..max).filter(|&i| g.guild.member[i].online != 0).count();
    // Remove guild from memory if no players online
    if online_count == 0 {
        g.save_flag |= GS_REMOVE;
    }
    put_cache(c, &g);
    1
}

/// int_guild.cpp search_guildname: the guild id with this name (`WHERE name = '…'`, safestrnlen(str, NAME_LENGTH);
/// MySQL compares without regard to case or trailing spaces), 0 when none.
fn search_guildname(c: &Ctx, name: &str) -> i32 {
    let key = trunc(name, NAME_LENGTH + 1);
    let mut rows: Vec<u32> = c.db().guild_sql().iter().filter(|r| sql_name_eq(&r.name, &key)).map(|r| r.guild_id).collect();
    rows.sort_unstable();
    rows.first().map(|&id| id as i32).unwrap_or(0)
}

/// int_guild.cpp guild_check_empty.
fn guild_check_empty(g: &CharGuild) -> bool {
    !(0..g.guild.max()).any(|i| g.guild.member[i].account_id > 0)
}

/// int_guild.cpp guild_checkskill (char side: no skill-id range check beyond the index).
pub fn guild_checkskill(g: &MmoGuild, id: i32) -> i32 {
    let idx = id - GD_SKILLBASE;
    if idx < 0 || idx as usize >= MAX_GUILDSKILL {
        return 0;
    }
    g.skill[idx as usize].lv
}

/// int_guild.cpp guild_calcinfo: level-ups from the guild EXP, max members, average level, online count. Returns true
/// when the level / capacity / skill points changed (the new info was then sent to the map server).
fn guild_calcinfo(g: &mut CharGuild) -> bool {
    let before = g.guild.clone(); // Save guild current values
    if g.guild.guild_lv <= 0 {
        g.guild.guild_lv = 1;
    }
    g.guild.next_exp = get_nextexp(g.guild.guild_lv);
    // Consume guild exp and increase guild level
    while g.guild.exp >= g.guild.next_exp && g.guild.next_exp > 0 && g.guild.guild_lv < MAX_GUILDLEVEL {
        g.guild.exp -= g.guild.next_exp;
        g.guild.guild_lv += 1;
        g.guild.skill_point += 1;
        g.guild.next_exp = get_nextexp(g.guild.guild_lv);
    }
    // Set the max number of members, Guild Extention skill - currently adds 6 to max per skill lv.
    g.guild.max_member = (16 + guild_checkskill(&g.guild, GD_EXTENSION) * 6) as i16;
    if g.guild.max_member as usize > MAX_GUILD {
        log::error!("Guild {}:{} has capacity for too many guild members ({}), max supported is {}", g.guild.guild_id, g.guild.name, g.guild.max_member, MAX_GUILD);
        g.guild.max_member = MAX_GUILD as i16;
    }
    // Compute the guild average level (average_lv is an int16 accumulator)
    g.guild.average_lv = 0;
    g.guild.connect_member = 0;
    let mut cnt = 0i16;
    for i in 0..g.guild.max() {
        let m = &g.guild.member[i];
        if m.account_id > 0 {
            if m.lv >= 0 {
                g.guild.average_lv = g.guild.average_lv.wrapping_add(m.lv);
                cnt += 1;
            } else {
                log::warn!("Guild {}:{}, member {}:{} has an invalid level {}", g.guild.guild_id, g.guild.name, m.char_id, m.name, m.lv);
            }
            if g.guild.member[i].online != 0 {
                g.guild.connect_member += 1;
            }
        }
    }
    if cnt != 0 {
        g.guild.average_lv /= cnt;
    }
    // Check if guild stats has change
    if g.guild.max_member != before.max_member || g.guild.guild_lv != before.guild_lv || g.guild.skill_point != before.skill_point {
        g.save_flag |= GS_LEVEL;
        mapif_guild_info(&g.guild);
        return true;
    }
    false
}

// ------------------------------------------------------------------------------------------------ to the map server

/// int_guild.cpp mapif_guild_created (0x3830): guild id 0 = failure.
fn mapif_guild_created(account_id: u32, g: Option<&MmoGuild>) {
    if let Some(g) = g {
        log::info!("int_guild: Guild created ({} - {})", g.guild_id, g.name);
    }
    intif::send(Inter::GuildCreated { account_id, guild_id: g.map(|g| g.guild_id).unwrap_or(0) });
}

/// int_guild.cpp mapif_guild_noinfo (0x3831, length 8).
fn mapif_guild_noinfo(guild_id: i32) {
    log::warn!("int_guild: info not found {guild_id}");
    intif::send(Inter::GuildInfoAck { guild_id, guild: None });
}

/// int_guild.cpp mapif_guild_info (0x3831 with the whole mmo_guild).
fn mapif_guild_info(g: &MmoGuild) {
    intif::send(Inter::GuildInfoAck { guild_id: g.guild_id, guild: Some(Box::new(g.clone())) });
}

/// int_guild.cpp mapif_guild_memberadded (0x3832).
fn mapif_guild_memberadded(guild_id: i32, account_id: u32, char_id: u32, flag: u8) {
    intif::send(Inter::GuildMemberAdded { guild_id, account_id, char_id, flag });
}

/// int_guild.cpp mapif_guild_withdraw (0x3834: mes is 40 bytes, name NAME_LENGTH).
fn mapif_guild_withdraw(guild_id: i32, account_id: u32, char_id: u32, flag: u8, name: &str, mes: &str) {
    log::info!("int_guild: guild withdraw ({guild_id} - {account_id}: {name} - {mes})");
    intif::send(Inter::GuildWithdraw { guild_id, account_id, char_id, flag, name: name.to_string(), mes: mes.to_string() });
}

/// int_guild.cpp mapif_guild_memberinfoshort (0x3835).
fn mapif_guild_memberinfoshort(g: &MmoGuild, idx: usize) {
    let m = &g.member[idx];
    intif::send(Inter::GuildMemberInfoShortAck { guild_id: g.guild_id, account_id: m.account_id, char_id: m.char_id, online: m.online as u8, lv: m.lv as u16, class: m.class as u16 });
}

/// int_guild.cpp mapif_guild_broken (0x3836).
fn mapif_guild_broken(guild_id: i32, flag: u8) {
    log::info!("int_guild: Guild broken ({guild_id})");
    intif::send(Inter::GuildBroken { guild_id, flag });
}

/// int_guild.cpp mapif_guild_basicinfochanged (0x3839): only GBI_EXP is ever sent (with the u64 exp).
fn mapif_guild_basicinfochanged(guild_id: i32, kind: u16, value: i64) {
    intif::send(Inter::GuildBasicInfoChanged { guild_id, kind, value });
}

/// int_guild.cpp mapif_guild_memberinfochanged (0x383a).
fn mapif_guild_memberinfochanged(guild_id: i32, account_id: u32, char_id: u32, kind: u16, value: i64) {
    intif::send(Inter::GuildMemberInfoChanged { guild_id, account_id, char_id, kind, value });
}

/// int_guild.cpp mapif_guild_skillupack (0x383c).
fn mapif_guild_skillupack(guild_id: i32, skill_id: u16, account_id: u32) {
    intif::send(Inter::GuildSkillUpAck { guild_id, skill_id, account_id });
}

/// int_guild.cpp mapif_guild_alliance (0x383d).
fn mapif_guild_alliance(guild_id1: i32, guild_id2: i32, account_id1: u32, account_id2: u32, flag: u8, name1: &str, name2: &str) {
    intif::send(Inter::GuildAllianceAck { guild_id1, guild_id2, account_id1, account_id2, flag, name1: name1.to_string(), name2: name2.to_string() });
}

/// int_guild.cpp mapif_guild_position (0x383b).
fn mapif_guild_position(g: &MmoGuild, idx: usize) {
    intif::send(Inter::GuildPositionAck { guild_id: g.guild_id, idx: idx as i32, pos: g.position[idx].clone() });
}

/// int_guild.cpp mapif_guild_notice (0x383e).
fn mapif_guild_notice(g: &MmoGuild) {
    intif::send(Inter::GuildNoticeAck { guild_id: g.guild_id, mes1: g.mes1.clone(), mes2: g.mes2.clone() });
}

/// int_guild.cpp mapif_guild_emblem (0x383f).
fn mapif_guild_emblem(g: &MmoGuild) {
    intif::send(Inter::GuildEmblemAck { guild_id: g.guild_id, emblem_id: g.emblem_id, data: g.emblem_data.iter().copied().take(g.emblem_len.max(0) as usize).collect() });
}

/// int_guild.cpp mapif_guild_emblem_version (0x3841).
fn mapif_guild_emblem_version(g: &MmoGuild) {
    intif::send(Inter::GuildEmblemVersionAck { guild_id: g.guild_id, emblem_id: g.emblem_id });
}

/// int_guild.cpp mapif_guild_master_changed (0x3843; the time is sent as uint32).
fn mapif_guild_master_changed(g: &MmoGuild, aid: u32, cid: u32, time: i64) {
    intif::send(Inter::GuildMasterChanged { guild_id: g.guild_id, account_id: aid, char_id: cid, time: time as u32 as i64 });
}

// ------------------------------------------------------------------------------------------------ from the map server

/// int_guild.cpp mapif_parse_CreateGuild (0x3030).
pub fn mapif_parse_create_guild(c: &Ctx, account_id: u32, name: &str, master: GuildMember) {
    if search_guildname(c, name) != 0 {
        log::info!("int_guild: guild with same name exists [{name}]");
        mapif_guild_created(account_id, None);
        return;
    }
    // Check Authorised letters/symbols in the name of the character
    let nb: Vec<u8> = crate::net::wire::latin1(name).into_iter().take(NAME_LENGTH).take_while(|&b| b != 0).collect();
    if CHAR_NAME_OPTION == 1 {
        // only letters/symbols in char_name_letters are authorised
        if nb.iter().any(|b| !crate::rules::CHAR_NAME_LETTERS.as_bytes().contains(b)) {
            mapif_guild_created(account_id, None);
            return;
        }
    }
    let mut g = MmoGuild::empty();
    g.name = trunc(name, NAME_LENGTH + 1);
    g.master = trunc(&master.name, NAME_LENGTH + 1);
    g.member[0] = master.clone();
    g.member[0].modified = GS_MEMBER_MODIFIED;
    // Set default positions
    g.position[0].mode = GUILD_PERM_DEFAULT;
    g.position[0].name = "GuildMaster".to_string();
    g.position[MAX_GUILDPOSITION - 1].name = "Newbie".to_string();
    g.position[0].modified = GS_POSITION_MODIFIED;
    g.position[MAX_GUILDPOSITION - 1].modified = GS_POSITION_MODIFIED;
    for i in 1..MAX_GUILDPOSITION - 1 {
        g.position[i].name = format!("Position {}", i + 1);
        g.position[i].modified = GS_POSITION_MODIFIED;
    }
    // Initialize guild property
    g.max_member = 16;
    g.average_lv = master.lv;
    g.connect_member = 1;
    for i in 0..MAX_GUILDSKILL {
        g.skill[i].id = i as i32 + GD_SKILLBASE;
    }
    g.guild_id = -1; // Request to create guild.
    // Create the guild
    if !inter_guild_tosql(c, &mut g, GS_BASIC | GS_POSITION | GS_SKILL | GS_MEMBER) {
        log::error!("Failed to create Guild {} (Guild Master: {})", g.name, g.master);
        mapif_guild_created(account_id, None);
        return;
    }
    log::info!("Created Guild {} - {} (Guild Master: {})", g.guild_id, g.name, g.master);
    // Add to cache (a new CharGuild: save_flag 0)
    let cg = CharGuild { guild_id: g.guild_id as u32, guild: g, save_flag: 0 };
    put_cache(c, &cg);
    ensure_timer(c);
    // Report to client
    mapif_guild_created(account_id, Some(&cg.guild));
    mapif_guild_info(&cg.guild);
    // (log_inter: "guild %s (id=%d) created by master %s (id=%d)")
}

/// int_guild.cpp mapif_parse_GuildInfo (0x3031).
pub fn mapif_parse_guild_info(c: &Ctx, guild_id: i32) {
    match inter_guild_fromsql(c, guild_id) {
        Some(mut g) => {
            if !guild_calcinfo(&mut g) {
                mapif_guild_info(&g.guild);
            }
            put_cache(c, &g);
        }
        None => mapif_guild_noinfo(guild_id), // Failed to load info
    }
}

/// int_guild.cpp mapif_parse_GuildAddMember (0x3032).
pub fn mapif_parse_guild_add_member(c: &Ctx, guild_id: i32, m: GuildMember) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else {
        // Failed to add
        mapif_guild_memberadded(guild_id, m.account_id, m.char_id, 1);
        return;
    };
    // Find an empty slot
    for i in 0..g.guild.max() {
        if g.guild.member[i].account_id == 0 {
            g.guild.member[i] = m.clone();
            g.guild.member[i].modified = GS_MEMBER_NEW | GS_MEMBER_MODIFIED;
            mapif_guild_memberadded(guild_id, m.account_id, m.char_id, 0);
            if !guild_calcinfo(&mut g) {
                // Send members if it was not invoked.
                mapif_guild_info(&g.guild);
            }
            g.save_flag |= GS_MEMBER;
            if g.save_flag & GS_REMOVE != 0 {
                g.save_flag &= !GS_REMOVE;
            }
            put_cache(c, &g);
            return;
        }
    }
    // Failed to add
    mapif_guild_memberadded(guild_id, m.account_id, m.char_id, 1);
}

/// int_guild.cpp mapif_parse_GuildLeave (0x3034; also inter_guild_leave for a deleted character).
pub fn mapif_parse_guild_leave(c: &Ctx, guild_id: i32, account_id: u32, char_id: u32, flag: u8, mes: &str) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else {
        // Unknown guild, just update the player: UPDATE char SET guild_id = 0 WHERE account_id AND char_id
        sql_set_char_guild(c, char_id, Some(account_id), None, 0);
        return;
    };
    // Find the member
    let Some(i) = (0..g.guild.max()).find(|&i| g.guild.member[i].account_id == account_id && g.guild.member[i].char_id == char_id) else {
        return; // TODO member not found
    };
    if flag != 0 {
        // Write expulsion reason: find an empty slot
        let j = match (0..MAX_GUILDEXPULSION).find(|&j| g.guild.expulsion[j].account_id == 0) {
            Some(j) => j,
            None => {
                // Expulsion list is full, flush the oldest one
                for j in 0..MAX_GUILDEXPULSION - 1 {
                    g.guild.expulsion[j] = g.guild.expulsion[j + 1].clone();
                }
                MAX_GUILDEXPULSION - 1
            }
        };
        // Save the expulsion entry
        g.guild.expulsion[j].account_id = account_id;
        g.guild.expulsion[j].char_id = char_id;
        g.guild.expulsion[j].name = trunc(&g.guild.member[i].name, NAME_LENGTH);
        g.guild.expulsion[j].mes = trunc(mes, 40);
    }
    let name = g.guild.member[i].name.clone();
    mapif_guild_withdraw(guild_id, account_id, char_id, flag, &name, mes);
    inter_guild_removemember_tosql(c, g.guild.member[i].char_id);
    g.guild.member[i] = GuildMember::default();
    if guild_check_empty(&g) {
        put_cache(c, &g);
        mapif_parse_break_guild(c, guild_id); // Break the guild.
    } else {
        // Update member info.
        if !guild_calcinfo(&mut g) {
            mapif_guild_info(&g.guild);
        }
        g.save_flag |= GS_EXPULSION;
        put_cache(c, &g);
    }
}

/// int_guild.cpp mapif_parse_GuildChangeMemberInfoShort (0x3035): a member logged in / out or levelled.
pub fn mapif_parse_guild_change_member_info_short(c: &Ctx, guild_id: i32, account_id: u32, char_id: u32, online: u8, lv: u16, class: u16) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else { return };
    if let Some(i) = (0..g.guild.max()).find(|&i| g.guild.member[i].account_id == account_id && g.guild.member[i].char_id == char_id) {
        g.guild.member[i].online = online as i16;
        g.guild.member[i].lv = lv as i16;
        g.guild.member[i].class = class as i16;
        g.guild.member[i].modified = GS_MEMBER_MODIFIED;
        mapif_guild_memberinfoshort(&g.guild, i);
    }
    let prev_count = g.guild.connect_member;
    let prev_alv = g.guild.average_lv;
    g.guild.average_lv = 0;
    g.guild.connect_member = 0;
    let mut cnt = 0i32;
    let mut sum = 0i32;
    for i in 0..g.guild.max() {
        if g.guild.member[i].account_id > 0 {
            sum += g.guild.member[i].lv as i32;
            cnt += 1;
        }
        if g.guild.member[i].online != 0 {
            g.guild.connect_member += 1;
        }
    }
    if cnt != 0 {
        // this check should always succeed...
        g.guild.average_lv = (sum / cnt) as i16;
        if g.guild.connect_member != prev_count || g.guild.average_lv != prev_alv {
            g.save_flag |= GS_CONNECT;
        }
        if g.save_flag & GS_REMOVE != 0 {
            g.save_flag &= !GS_REMOVE;
        }
    }
    g.save_flag |= GS_MEMBER; // Update guild member data
    put_cache(c, &g);
}

/// int_guild.cpp mapif_parse_BreakGuild (0x3036; char_delete also calls it for a deleted guild master).
pub fn mapif_parse_break_guild(c: &Ctx, guild_id: i32) {
    let Some(g) = inter_guild_fromsql(c, guild_id) else { return };
    let gid = guild_id as u32;
    // Delete guild from sql
    c.db().guild_sql().guild_id().delete(gid);
    for id in c.db().guild_member_sql().by_guild().filter(gid).map(|r| r.id).collect::<Vec<_>>() {
        c.db().guild_member_sql().id().delete(id);
    }
    // DELETE FROM guild_castle WHERE guild_id (woe/int_castle.rs)
    crate::woe::int_castle::break_guild_castles(c, guild_id);
    crate::guild_storage::sql_delete_guild_storage(c, gid);
    // DELETE FROM guild_alliance WHERE guild_id OR alliance_id
    let mut al: Vec<u64> = c.db().guild_alliance_sql().by_guild().filter(gid).map(|r| r.id).collect();
    al.extend(c.db().guild_alliance_sql().alliance_id().filter(gid).map(|r| r.id));
    for id in al {
        c.db().guild_alliance_sql().id().delete(id);
    }
    for id in c.db().guild_position_sql().by_guild().filter(gid).map(|r| r.id).collect::<Vec<_>>() {
        c.db().guild_position_sql().id().delete(id);
    }
    for id in c.db().guild_skill_sql().by_guild().filter(gid).map(|r| r.id).collect::<Vec<_>>() {
        c.db().guild_skill_sql().id().delete(id);
    }
    for id in c.db().guild_expulsion_sql().by_guild().filter(gid).map(|r| r.id).collect::<Vec<_>>() {
        c.db().guild_expulsion_sql().id().delete(id);
    }
    // UPDATE char SET guild_id = 0 WHERE guild_id
    let ids: Vec<u32> = c.db().character().iter().filter(|ch| ch.guild_id == gid).map(|ch| ch.id).collect();
    for id in ids {
        sql_set_char_guild(c, id, None, Some(gid), 0);
    }
    mapif_guild_broken(guild_id, 0);
    // (log_inter: "guild %s (id=%d) broken")
    log::info!("guild {} (id={guild_id}) broken", g.guild.name);
    // Remove the guild from memory. [Skotlex]
    c.db().inter_guild().guild_id().delete(gid);
}

/// int_guild.cpp mapif_parse_GuildBasicInfoChange (0x3039): only GBI_GUILDLV is handled (@guildlevelup); it answers
/// with the whole guild info and returns before the basicinfochanged notice.
pub fn mapif_parse_guild_basic_info_change(c: &Ctx, guild_id: i32, kind: u16, value: i64) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else { return };
    let data_value = value as i16;
    match kind {
        GBI_GUILDLV => {
            if data_value > 0 && g.guild.guild_lv + data_value <= MAX_GUILDLEVEL {
                g.guild.guild_lv += data_value;
                g.guild.skill_point += data_value as i32;
            } else if data_value < 0 && g.guild.guild_lv + data_value >= 1 {
                g.guild.guild_lv += data_value;
            }
            mapif_guild_info(&g.guild);
            g.save_flag |= GS_LEVEL;
            put_cache(c, &g);
            return;
        }
        _ => log::error!("int_guild: GuildBasicInfoChange: Unknown type {kind}"),
    }
    mapif_guild_basicinfochanged(guild_id, kind, value);
}

/// int_guild.cpp mapif_parse_GuildMemberInfoChange (0x303a).
pub fn mapif_parse_guild_member_info_change(c: &Ctx, guild_id: i32, account_id: u32, char_id: u32, kind: u16, value: i64) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else { return };
    // Search the member
    let Some(i) = (0..g.guild.max()).find(|&i| g.guild.member[i].account_id == account_id && g.guild.member[i].char_id == char_id) else {
        log::warn!("int_guild: GuildMemberChange: Not found {account_id},{char_id} in guild ({guild_id} - {})", g.guild.name);
        return;
    };
    match kind {
        GMI_POSITION => {
            g.guild.member[i].position = value as i16;
            g.guild.member[i].modified = GS_MEMBER_MODIFIED;
            mapif_guild_memberinfochanged(guild_id, account_id, char_id, kind, value);
            g.save_flag |= GS_MEMBER;
        }
        GMI_EXP => {
            let old_exp = g.guild.member[i].exp;
            g.guild.member[i].exp = value as u64;
            g.guild.member[i].modified = GS_MEMBER_MODIFIED;
            if g.guild.member[i].exp > old_exp {
                let mut exp = g.guild.member[i].exp - old_exp;
                // Compute gained exp
                if GUILD_EXP_RATE != 100 {
                    exp = exp * GUILD_EXP_RATE / 100;
                }
                // Update guild exp
                g.guild.exp = g.guild.exp.saturating_add(exp).min(MAX_GUILD_EXP);
                guild_calcinfo(&mut g);
                mapif_guild_basicinfochanged(guild_id, GBI_EXP, g.guild.exp as i64);
                g.save_flag |= GS_LEVEL;
            }
            mapif_guild_memberinfochanged(guild_id, account_id, char_id, kind, value);
            g.save_flag |= GS_MEMBER;
        }
        GMI_HAIR | GMI_HAIR_COLOR | GMI_GENDER | GMI_CLASS | GMI_LEVEL => {
            let v = value as i16;
            match kind {
                GMI_HAIR => g.guild.member[i].hair = v,
                GMI_HAIR_COLOR => g.guild.member[i].hair_color = v,
                GMI_GENDER => g.guild.member[i].gender = v,
                GMI_CLASS => g.guild.member[i].class = v,
                _ => g.guild.member[i].lv = v,
            }
            g.guild.member[i].modified = GS_MEMBER_MODIFIED;
            mapif_guild_memberinfochanged(guild_id, account_id, char_id, kind, value);
            g.save_flag |= GS_MEMBER; // Save new data.
        }
        _ => log::error!("int_guild: GuildMemberInfoChange: Unknown type {kind}"),
    }
    put_cache(c, &g);
}

/// int_guild.cpp inter_guild_sex_changed (char_logif.cpp chlogif_parse_change_sex_sub: atcommand_accounts.rs).
pub fn inter_guild_sex_changed(c: &Ctx, guild_id: i32, account_id: u32, char_id: u32, gender: i16) {
    mapif_parse_guild_member_info_change(c, guild_id, account_id, char_id, GMI_GENDER, gender as i64);
}

/// int_guild.cpp inter_guild_charname_changed (char.cpp's rename; the module has no character rename yet).
pub fn inter_guild_charname_changed(c: &Ctx, guild_id: i32, char_id: u32, name: &str) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else {
        log::error!("inter_guild_charrenamed: Can't find guild {guild_id}.");
        return;
    };
    let Some(i) = (0..g.guild.max()).find(|&i| g.guild.member[i].char_id == char_id) else {
        log::error!("inter_guild_charrenamed: Can't find character {char_id} in the guild");
        return;
    };
    let mut flag = 0;
    if g.guild.member[i].name == g.guild.master {
        g.guild.master = trunc(name, NAME_LENGTH);
        flag |= GS_BASIC;
    }
    g.guild.member[i].name = trunc(name, NAME_LENGTH);
    g.guild.member[i].modified = GS_MEMBER_MODIFIED;
    flag |= GS_MEMBER;
    if !inter_guild_tosql(c, &mut g.guild, flag) {
        return;
    }
    put_cache(c, &g);
    mapif_guild_info(&g.guild);
}

/// int_guild.cpp mapif_parse_GuildPosition (0x303b).
pub fn mapif_parse_guild_position(c: &Ctx, guild_id: i32, idx: i32, p: GuildPosition) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else { return };
    if idx < 0 || idx as usize >= MAX_GUILDPOSITION {
        return;
    }
    let idx = idx as usize;
    g.guild.position[idx] = p;
    mapif_guild_position(&g.guild, idx);
    g.guild.position[idx].modified = GS_POSITION_MODIFIED;
    g.save_flag |= GS_POSITION; // Change guild_position
    put_cache(c, &g);
}

/// int_guild.cpp mapif_parse_GuildSkillUp (0x303c).
pub fn mapif_parse_guild_skill_up(c: &Ctx, guild_id: i32, skill_id: u16, account_id: u32, max: i32) {
    let idx = skill_id as i32 - GD_SKILLBASE;
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else { return };
    if idx < 0 || idx as usize >= MAX_GUILDSKILL {
        return;
    }
    let idx = idx as usize;
    if g.guild.skill_point > 0 && g.guild.skill[idx].id > 0 && g.guild.skill[idx].lv < max {
        g.guild.skill[idx].lv += 1;
        g.guild.skill_point -= 1;
        if !guild_calcinfo(&mut g) {
            mapif_guild_info(&g.guild);
        }
        mapif_guild_skillupack(guild_id, skill_id, account_id);
        g.save_flag |= GS_LEVEL | GS_SKILL; // Change guild & guild_skill
        if skill_id as i32 == GD_GUILD_STORAGE {
            // Force save for GD_GUILD_STORAGE (the flags stay set: the save timer writes them again)
            let flag = g.save_flag;
            inter_guild_tosql(c, &mut g.guild, flag);
        }
        put_cache(c, &g);
    }
}

/// int_guild.cpp mapif_parse_GuildDeleteAlliance: manual deletion of an alliance when the partnering guild does not
/// exist. [Skotlex]
fn mapif_parse_guild_delete_alliance(c: &Ctx, g: &mut CharGuild, guild_id: i32, account_id1: u32, account_id2: u32, flag: i32) -> i32 {
    let Some(i) = (0..MAX_GUILDALLIANCE).find(|&i| g.guild.alliance[i].guild_id == guild_id) else { return -1 };
    let name = g.guild.alliance[i].name.clone();
    g.guild.alliance[i].guild_id = 0;
    mapif_guild_alliance(g.guild.guild_id, guild_id, account_id1, account_id2, flag as u8, &g.guild.name, &name);
    g.save_flag |= GS_ALLIANCE;
    put_cache(c, g);
    0
}

/// int_guild.cpp mapif_parse_GuildAlliance (0x303d): adds / removes an alliance (both sides) or an opposition.
pub fn mapif_parse_guild_alliance(c: &Ctx, guild_id1: i32, guild_id2: i32, account_id1: u32, account_id2: u32, flag: i32) -> i32 {
    let g0 = inter_guild_fromsql(c, guild_id1);
    let g1 = inter_guild_fromsql(c, guild_id2);
    // Requested to remove an alliance with a not found guild.
    if let (Some(mut a), None) = (g0.clone(), g1.as_ref()) {
        if flag & GUILD_ALLIANCE_REMOVE != 0 {
            return mapif_parse_guild_delete_alliance(c, &mut a, guild_id2, account_id1, account_id2, flag); // Try to do a manual removal of said guild.
        }
    }
    let (Some(a), Some(b)) = (g0, g1) else { return 0 };
    let mut g = [a, b];
    // (the same guild twice: both references are one object in rAthena)
    let same = g[0].guild_id == g[1].guild_id;
    let n = 2 - (flag & GUILD_ALLIANCE_TYPE_MASK) as usize;
    if flag & GUILD_ALLIANCE_REMOVE != 0 {
        // Remove alliance/opposition, in case of alliance, remove on both side
        for i in 0..n {
            if same && i == 1 {
                g[1] = g[0].clone();
            }
            let other = g[1 - i].guild.guild_id;
            if let Some(j) = (0..MAX_GUILDALLIANCE).find(|&j| g[i].guild.alliance[j].guild_id == other && g[i].guild.alliance[j].opposition == flag & GUILD_ALLIANCE_TYPE_MASK) {
                g[i].guild.alliance[j].guild_id = 0;
            }
            if same {
                g[1 - i] = g[i].clone();
            }
        }
    } else {
        // Add alliance, in case of alliance, add on both side
        for i in 0..n {
            if let Some(j) = (0..MAX_GUILDALLIANCE).find(|&j| g[i].guild.alliance[j].guild_id == 0) {
                let (oid, oname) = (g[1 - i].guild.guild_id, g[1 - i].guild.name.clone());
                g[i].guild.alliance[j].guild_id = oid;
                g[i].guild.alliance[j].name = oname;
                // Set alliance type
                g[i].guild.alliance[j].opposition = flag & GUILD_ALLIANCE_TYPE_MASK;
            }
            if same {
                g[1 - i] = g[i].clone();
            }
        }
    }
    // Send on all map the new alliance/opposition
    mapif_guild_alliance(guild_id1, guild_id2, account_id1, account_id2, flag as u8, &g[0].guild.name, &g[1].guild.name);
    // Mark the two guild to be saved
    g[0].save_flag |= GS_ALLIANCE;
    g[1].save_flag |= GS_ALLIANCE;
    put_cache(c, &g[0]);
    put_cache(c, &g[1]);
    1
}

/// int_guild.cpp mapif_parse_GuildNotice (0x303e): saved at once.
pub fn mapif_parse_guild_notice(c: &Ctx, guild_id: i32, mes1: &str, mes2: &str) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else { return };
    g.guild.mes1 = trunc(mes1, MAX_GUILDMES1 + 1);
    g.guild.mes2 = trunc(mes2, MAX_GUILDMES2 + 1);
    g.save_flag |= GS_MES; // Change mes of guild
    let flag = g.save_flag;
    inter_guild_tosql(c, &mut g.guild, flag);
    put_cache(c, &g);
    mapif_guild_notice(&g.guild);
}

/// int_guild.cpp mapif_parse_GuildEmblem (0x303f).
pub fn mapif_parse_guild_emblem(c: &Ctx, guild_id: i32, data: &[u8]) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else { return };
    let len = data.len().min(EMBLEM_DATA_SIZE);
    g.guild.emblem_data = data[..len].to_vec();
    g.guild.emblem_len = len as i32;
    g.guild.emblem_id += 1;
    g.save_flag |= GS_EMBLEM; // Change guild
    put_cache(c, &g);
    mapif_guild_emblem(&g.guild);
}

/// int_guild.cpp mapif_parse_GuildMasterChange (0x3033): `name` is the new master's name (len bytes, NUL included).
pub fn mapif_parse_guild_master_change(c: &Ctx, guild_id: i32, name: &str, len: usize) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else { return };
    if len > NAME_LENGTH {
        return;
    }
    // Find member (name)
    let Some(pos) = (0..g.guild.max()).find(|&p| strncmp_eq(&g.guild.member[p].name, name, len)) else {
        return; // Character not found??
    };
    // Switch current and old GM
    g.guild.member.swap(pos, 0);
    // Switch positions
    g.guild.member[pos].position = g.guild.member[0].position;
    g.guild.member[pos].modified = GS_MEMBER_MODIFIED;
    g.guild.member[0].position = 0; // Position 0: guild Master.
    g.guild.member[0].modified = GS_MEMBER_MODIFIED;
    // Store changing time
    g.guild.last_leader_change = c.tick() / 1000;
    // safestrncpy(master, name, len); master[len] = '\0' (len counts the NUL)
    g.guild.master = trunc(name, len);
    log::info!("int_guild: Guildmaster Changed to {} (Guild {guild_id} - {})", g.guild.master, g.guild.name);
    g.save_flag |= GS_BASIC | GS_MEMBER; // Save main data and member data.
    put_cache(c, &g);
    mapif_guild_master_changed(&g.guild, g.guild.member[0].account_id, g.guild.member[0].char_id, g.guild.last_leader_change);
}

/// int_guild.cpp mapif_parse_GuildEmblemVersion (0x3042).
pub fn mapif_parse_guild_emblem_version(c: &Ctx, guild_id: i32, version: i32) {
    let Some(mut g) = inter_guild_fromsql(c, guild_id) else { return };
    g.guild.emblem_len = 0;
    g.guild.emblem_id = version;
    g.save_flag |= GS_EMBLEM;
    put_cache(c, &g);
    mapif_guild_emblem_version(&g.guild);
}

/// int_guild.cpp inter_guild_leave: a deleted character leaves its guild (char.cpp char_delete).
pub fn inter_guild_leave(c: &Ctx, guild_id: i32, account_id: u32, char_id: u32) {
    mapif_parse_guild_leave(c, guild_id, account_id, char_id, 0, "** Character Deleted **");
}

/// char.cpp char_delete's guild part: a guild master breaks the guild (`SELECT guild_id FROM guild WHERE char_id`),
/// anyone else leaves it.
pub fn char_delete_guild(c: &Ctx, account_id: u32, char_id: u32, guild_id: u32) {
    let master_of = c.db().guild_sql().iter().find(|g| g.char_id == char_id).map(|g| g.guild_id);
    if let Some(gid) = master_of {
        mapif_parse_break_guild(c, gid as i32);
    } else if guild_id != 0 {
        inter_guild_leave(c, guild_id as i32, account_id, char_id); // Leave your guild.
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// db/re/exp_guild.yml: 49 levels (the char server reports "Done reading '49' entries").
    #[test]
    fn exp_table() {
        assert_eq!(guild_exp_db().exp.len(), 49);
        assert_eq!(get_nextexp(1), 100_000);
        assert_eq!(get_nextexp(49), 240_100_000);
        assert_eq!(get_nextexp(50), 0);
        assert_eq!(get_nextexp(0), 0);
    }

    #[test]
    fn guild_calcinfo_levels() {
        let mut g = CharGuild { guild_id: 1, guild: MmoGuild::empty(), save_flag: 0 };
        g.guild.guild_id = 1;
        g.guild.guild_lv = 1;
        g.guild.max_member = 16;
        g.guild.exp = 100_000 + 400_000 + 5;
        g.guild.member[0] = GuildMember { account_id: 1, char_id: 1, lv: 10, online: 1, ..Default::default() };
        g.guild.member[1] = GuildMember { account_id: 2, char_id: 2, lv: 21, online: 0, ..Default::default() };
        crate::intif::reset();
        assert!(guild_calcinfo(&mut g));
        crate::intif::reset();
        assert_eq!((g.guild.guild_lv, g.guild.skill_point, g.guild.exp, g.guild.next_exp), (3, 2, 5, 900_000));
        assert_eq!((g.guild.average_lv, g.guild.connect_member, g.guild.max_member), (15, 1, 16));
        assert_eq!(g.save_flag & GS_LEVEL, GS_LEVEL);
    }

    #[test]
    fn name_helpers() {
        assert_eq!(trunc("abcdefghijklmnopqrstuvwxyzABC", NAME_LENGTH), "abcdefghijklmnopqrstuvw");
        assert!(strncmp_eq("Alpha", "Alpha", 6));
        assert!(!strncmp_eq("Alpha", "Alph", 6));
        assert!(sql_name_eq("Loki Guild ", "loki guild"));
    }
}
