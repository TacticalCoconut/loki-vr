//! char/int_guild.cpp's castle data: the SQL table `guild_castle` (sql-files/main.sql: every column `int(11)
//! unsigned`), the char server's castle cache (int_guild.cpp's `castle_db`, an unordered_map filled by
//! inter_guildcastle_fromsql and never emptied while the char server runs), inter_guildcastle_tosql / fromsql,
//! mapif_guild_castle_dataload and the map server's requests mapif_parse_GuildCastleDataLoad (0x3040) and
//! mapif_parse_GuildCastleDataSave (0x3041), and mapif_parse_BreakGuild's `DELETE FROM guild_castle`.
//!
//! The cache and the SQL rows differ in two cases rAthena has too: BreakGuild deletes the guild's rows from SQL while
//! the cache keeps them (the map server's castle_guild_broken_sub saves owner 0 right after, which writes the cached
//! castle back), and a negative value never reaches SQL (REPLACE INTO an unsigned column fails under the oracle's
//! STRICT_TRANS_TABLES: the statement is refused and the old row stays) while the cache keeps it. The module's char
//! server never restarts, so the cache (like int_guild.rs's guild cache) lives as long as the database.

use spacetimedb::{table, Table};

use super::{CastleData, CD_CREATE_TIME, CD_CURRENT_DEFENSE, CD_CURRENT_ECONOMY, CD_ENABLED_GUARDIAN00, CD_ENABLED_KAFRA, CD_GUILD_ID, CD_INVESTED_DEFENSE, CD_INVESTED_ECONOMY, CD_MAX, CD_NEXT_TIME, CD_PAY_TIME, MAX_GUARDIANS};
use crate::intif::{self, Inter};
use crate::world::Ctx;

/// The SQL table `guild_castle` (visibleG0-7 as the 0 / 1 the char server writes).
#[table(accessor = guild_castle_sql)]
#[derive(Clone, Debug)]
pub struct GuildCastleSql {
    #[primary_key]
    pub castle_id: i32,
    pub data: CastleData,
}

/// int_guild.cpp castle_db: the char server's cache of the castles it loaded.
#[table(accessor = inter_castle)]
#[derive(Clone, Debug)]
pub struct InterCastle {
    #[primary_key]
    pub castle_id: i32,
    pub data: CastleData,
}

/// int_guild.cpp inter_guildcastle_tosql: `REPLACE INTO guild_castle SET …` with every field.
pub fn inter_guildcastle_tosql(c: &Ctx, gc: &CastleData) -> i32 {
    let d = gc;
    // every column is `int(11) unsigned`: MariaDB (STRICT_TRANS_TABLES) refuses the statement for a negative value
    let values = [d.castle_id, d.guild_id, d.economy, d.defense, d.trigger_e, d.trigger_d, d.next_time, d.pay_time, d.create_time, d.visible_c];
    if let Some(v) = values.iter().find(|&&v| v < 0) {
        // Sql_ShowDebug
        log::error!("inter_guildcastle_tosql: Out of range value {v} for an unsigned column of castle {} (the row is not saved)", d.castle_id);
        return 0;
    }
    let row = GuildCastleSql { castle_id: d.castle_id, data: d.clone() };
    if c.db().guild_castle_sql().castle_id().find(d.castle_id).is_some() {
        c.db().guild_castle_sql().castle_id().update(row);
    } else {
        c.db().guild_castle_sql().insert(row);
    }
    // (charserv_config.save_log: "Saved guild castle (%d)")
    log::debug!("Saved guild castle ({})", d.castle_id);
    0
}

/// int_guild.cpp inter_guildcastle_fromsql: the cached castle, else its SQL row (a castle without a row is all zero),
/// which joins the cache.
pub fn inter_guildcastle_fromsql(c: &Ctx, castle_id: i32) -> CastleData {
    if let Some(gc) = c.db().inter_castle().castle_id().find(castle_id) {
        return gc.data;
    }
    let mut gc = CastleData::empty(castle_id);
    if let Some(row) = c.db().guild_castle_sql().castle_id().find(castle_id) {
        gc = row.data;
        gc.castle_id = castle_id;
        gc.visible_g.resize(MAX_GUARDIANS, false);
    }
    put_cache(c, &gc);
    // (charserv_config.save_log: "Loaded guild castle (%d - guild %d)")
    log::debug!("Loaded guild castle ({castle_id} - guild {})", gc.guild_id);
    gc
}

fn put_cache(c: &Ctx, gc: &CastleData) {
    let row = InterCastle { castle_id: gc.castle_id, data: gc.clone() };
    if c.db().inter_castle().castle_id().find(gc.castle_id).is_some() {
        c.db().inter_castle().castle_id().update(row);
    } else {
        c.db().inter_castle().insert(row);
    }
}

/// int_guild.cpp mapif_parse_GuildCastleDataLoad → mapif_guild_castle_dataload (0x3840): every requested castle, in
/// the request's order.
pub fn mapif_parse_guild_castle_data_load(c: &Ctx, castle_ids: &[i32]) {
    let castles: Vec<CastleData> = castle_ids.iter().map(|&id| inter_guildcastle_fromsql(c, id)).collect();
    intif::send(Inter::GuildCastleDataLoadAck { castles });
}

/// int_guild.cpp mapif_parse_GuildCastleDataSave: one field of a castle, then the whole castle to SQL.
pub fn mapif_parse_guild_castle_data_save(c: &Ctx, castle_id: i32, index: i32, value: i32) -> i32 {
    let mut gc = inter_guildcastle_fromsql(c, castle_id);
    match index {
        CD_GUILD_ID => {
            if gc.guild_id != value {
                // (charserv_config.log_inter: inter_log "guild %s (id=%d) %s castle id=%d")
                let gid = if value != 0 { value } else { gc.guild_id };
                let name = c.db().inter_guild().guild_id().find(gid.max(0) as u32).map(|g| g.guild.name).unwrap_or_else(|| "??".into());
                log::info!("guild {name} (id={gid}) {} castle id={castle_id}", if value != 0 { "occupy" } else { "abandon" });
            }
            gc.guild_id = value;
        }
        CD_CURRENT_ECONOMY => gc.economy = value,
        CD_CURRENT_DEFENSE => gc.defense = value,
        CD_INVESTED_ECONOMY => gc.trigger_e = value,
        CD_INVESTED_DEFENSE => gc.trigger_d = value,
        CD_NEXT_TIME => gc.next_time = value,
        CD_PAY_TIME => gc.pay_time = value,
        CD_CREATE_TIME => gc.create_time = value,
        CD_ENABLED_KAFRA => gc.visible_c = value,
        _ => {
            if (CD_ENABLED_GUARDIAN00..CD_MAX).contains(&index) {
                gc.visible_g[(index - CD_ENABLED_GUARDIAN00) as usize] = value & 1 != 0;
            } else {
                log::error!("mapif_parse_GuildCastleDataSave: not found index={index}");
                return 0;
            }
        }
    }
    put_cache(c, &gc);
    inter_guildcastle_tosql(c, &gc);
    0
}

/// int_guild.cpp mapif_parse_BreakGuild: `DELETE FROM guild_castle WHERE guild_id = …` (SQL only: the cache keeps the
/// castles).
pub fn break_guild_castles(c: &Ctx, guild_id: i32) {
    let ids: Vec<i32> = c.db().guild_castle_sql().iter().filter(|r| r.data.guild_id == guild_id).map(|r| r.castle_id).collect();
    for id in ids {
        c.db().guild_castle_sql().castle_id().delete(id);
    }
}

use crate::int_guild::inter_guild as _;
