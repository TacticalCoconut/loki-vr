//! mob.cpp's castle monsters: the guardians the `guardian` script command places (mob_spawn_guardian, permanent ones
//! in the castle's guardian slots, temporary ones in temp_guardians), the Emperium's castle data (mob_once_spawn's
//! MOBID_EMPERIUM branch), mob_spawn_guardian_sub (the guild data once the guild is known), mob_guardian_guildchange,
//! and what other files do with md->guardian_data: the castle bonus of status_calc_mob_, clif_name's guild and castle
//! names, mob_dead's slot save and unit_free's slot release.
//!
//! md->guardian_data is the `guardian_data` row of the monster (none = nullptr); mob_data's `guardian` flag (the mob
//! agent's `md->guardian_data != nullptr`) follows it. The castle pointer is the castle id.

use spacetimedb::{table, Table};

use super::{castle_rt, guild_castledatasave, put_castle_rt, CD_ENABLED_GUARDIAN00, CD_GUILD_ID, MAX_GUARDIANS};
use crate::db::misc::castle::castle_db;
use crate::db::mobs::MOBID_EMPERIUM;
use crate::status::data::StatusData;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// guild.hpp struct guardian_data.
#[table(accessor = guardian_data)]
#[derive(Clone, Debug)]
pub struct GuardianData {
    /// The monster (block_list id).
    #[primary_key]
    pub id: u32,
    /// 0 to MAX_GUARDIANS-1 = Guardians, MAX_GUARDIANS = Emperium, -1 = a temporary guardian.
    pub number: i32,
    pub guild_id: i32,
    pub emblem_id: i32,
    /// Level of GD_GUARDUP skill.
    pub guardup_lv: i32,
    /// char guild_name[NAME_LENGTH].
    pub guild_name: String,
    /// md->guardian_data->castle.
    pub castle_id: i32,
}

/// md->guardian_data.
pub fn get(c: &Ctx, id: u32) -> Option<GuardianData> {
    c.db().guardian_data().id().find(id)
}

pub fn put(c: &Ctx, gd: &GuardianData) {
    if c.db().guardian_data().id().find(gd.id).is_some() {
        c.db().guardian_data().id().update(gd.clone());
    } else {
        c.db().guardian_data().insert(gd.clone());
    }
}

/// md->guardian_data = … (aCalloc + placement new): the row, and mob_data's flag.
fn attach(c: &Ctx, gd: &GuardianData) {
    put(c, gd);
    if let Some(mut md) = crate::mob::get(c, gd.id) {
        if !md.guardian {
            md.guardian = true;
            crate::mob::put(c, &md);
        }
    }
}

/// md->guardian_data->guardup_lv (0 without guardian data).
pub fn guardup_lv(c: &Ctx, id: u32) -> i32 {
    get(c, id).map(|g| g.guardup_lv).unwrap_or(0)
}

/// mmo.hpp NAME_LENGTH copies (memcpy of the guild name: at most 23 characters before the NUL).
fn name24(s: &str) -> String {
    s.chars().take(crate::mob::NAME_LENGTH - 1).collect()
}

// ------------------------------------------------------------------------------------------------ spawning

/// mob.cpp mob_once_spawn's MOBID_EMPERIUM branch (after mob_once_spawn_sub, before mob_spawn): on a castle map the
/// Emperium gets guardian data for slot MAX_GUARDIANS with the castle's owner, and its guild's emblem and name when
/// the guild is loaded (else mob_spawn_guardian_sub fills them in after mob_respawn_time).
pub fn mob_once_spawn_emperium(c: &Ctx, md_id: u32, m: u16) {
    let Some(gc) = super::castle_of_map(m) else { return };
    let owner = castle_rt(c, gc.castle_id).data.guild_id;
    let g = crate::guild::guild_search(c, owner);
    let mut gd = GuardianData { id: md_id, number: MAX_GUARDIANS as i32, guild_id: owner, emblem_id: 0, guardup_lv: 0, guild_name: String::new(), castle_id: gc.castle_id };
    if let Some(g) = &g {
        gd.emblem_id = g.guild.emblem_id;
        gd.guild_name = name24(&g.guild.name);
    }
    attach(c, &gd);
    if g.is_none() && owner != 0 {
        // Guild is not yet available, retry after the configured timespan.
        crate::timer::add(c, c.tick() + crate::conf::mob_respawn_time(), crate::timer::Func::MobSpawnGuardianSub, md_id, owner as i64);
    }
}

/// mob.cpp mob_spawn_guardian_sub (timer, data = the guild id): sets a guardian's guild data, which may not have been
/// available at spawn time. A guild that still cannot be found frees the castle (Emperium) or removes the guardian.
pub fn mob_spawn_guardian_sub(c: &Ctx, id: u32, data: i64) -> i32 {
    let Some(bl) = unit::get(c, id) else {
        return 0; // It is possible mob was already removed from map when the castle has no owner. [Skotlex]
    };
    if bl.kind != crate::map::BL_MOB {
        log::error!("mob_spawn_guardian_sub: Block error!");
        return 0;
    }
    let Some(md) = crate::mob::get(c, id) else { return 0 };
    let Some(mut gd) = get(c, id) else { return 0 }; // nullpo_ret(md->guardian_data)
    let guild_id = data as i32;
    let Some(g) = crate::guild::guild_search(c, guild_id) else {
        // Liberate castle, if the guild is not found this is an error! [Skotlex]
        log::error!("mob_spawn_guardian_sub: Couldn't load guild {guild_id}!");
        if md.mob_id == MOBID_EMPERIUM as i32 {
            // Not sure this is the best way, but otherwise we'd be invoking this for ALL guardians spawned later on.
            gd.guild_id = 0;
            put(c, &gd);
            if castle_rt(c, gd.castle_id).data.guild_id != 0 {
                // Free castle up.
                let name = castle_db().find(gd.castle_id).map(|g| g.castle_name.clone()).unwrap_or_default();
                log::info!("Clearing ownership of castle {} ({name})", gd.castle_id);
                guild_castledatasave(c, gd.castle_id, CD_GUILD_ID, 0);
            }
        } else {
            if gd.number >= 0 && gd.number < MAX_GUARDIANS as i32 && castle_rt(c, gd.castle_id).data.visible_g[gd.number as usize] {
                guild_castledatasave(c, gd.castle_id, CD_ENABLED_GUARDIAN00 + gd.number, 0);
            }
            crate::mob::unit::unit_free(c, id, unit::CLR_OUTSIGHT); // Remove guardian.
        }
        return 0;
    };
    let lv = crate::guild::guild_checkskill(&g.guild, crate::db::skills::ids::GD_GUARDUP as i32);
    gd.emblem_id = g.guild.emblem_id;
    gd.guild_name = name24(&g.guild.name);
    gd.guardup_lv = lv;
    put(c, &gd);
    if lv != 0 {
        crate::mob::status::status_calc_mob(c, id, crate::status::SCO_NONE); // Give bonuses.
    }
    0
}

/// mob.cpp mob_spawn_guardian: summons guardian `mob_id` at (x, y) of a castle map ((0, 0) or less: a random free
/// cell), in the castle's guardian slot `guardian` when `has_index`, else as a temporary guardian. Returns its id
/// (0 on failure).
#[allow(clippy::too_many_arguments)]
pub fn mob_spawn_guardian(c: &Ctx, mapname: &str, x: i16, y: i16, mobname: &str, mob_id: i32, event: &str, guardian: i32, has_index: bool) -> u32 {
    let Some(m) = crate::data::maps().id_of(mapname) else {
        log::warn!("mob_spawn_guardian: Map [{mapname}] not found.");
        return 0;
    };
    let mut mob_id = mob_id;
    if mob_id <= 0 {
        mob_id = crate::mob::spawn::get_random_id(c, -mob_id - 1, crate::db::mobs::RMF_DB_RATE, 0);
        if mob_id == 0 {
            return 0;
        }
    }
    let guardian = if !has_index {
        -1
    } else if !(0..MAX_GUARDIANS as i32).contains(&guardian) {
        log::error!("mob_spawn_guardian: Invalid guardian index {guardian} for guardian {mob_id} (castle map {mapname})");
        return 0;
    } else {
        guardian
    };
    let (mut x, mut y) = (x, y);
    if x <= 0 || y <= 0 {
        match crate::mob::spawn::map_search_freecell(c, None, m as i32, x, y, -1, -1, 1, 50) {
            Some((nx, ny)) => {
                x = nx;
                y = ny;
            }
            None => {
                log::warn!("mob_spawn_guardian: Couldn't locate a spawn cell for guardian class {mob_id} (index {guardian}) at castle map {mapname}");
                return 0;
            }
        }
    }
    // safestrncpy(data.name, mobname, NAME_LENGTH); safestrncpy(data.eventname, event, EVENT_NAME_LENGTH)
    let mut name: String = mobname.chars().take(crate::mob::NAME_LENGTH - 1).collect();
    let mut eventname: String = event.chars().take(crate::script::npc_run::EVENT_NAME_LENGTH - 1).collect();
    if !crate::mob::spawn::mob_parse_dataset(c, mob_id, 1, &mut name, &mut eventname) {
        return 0;
    }
    let Some(gc) = castle_db().mapname2gc(mapname) else {
        log::error!("mob_spawn_guardian: No castle set at map {mapname}");
        return 0;
    };
    let owner = castle_rt(c, gc.castle_id).data.guild_id;
    let g = if owner == 0 {
        log::warn!("mob_spawn_guardian: Spawning guardian {mob_id} on a castle with no guild (castle map {mapname})");
        None
    } else {
        crate::guild::guild_search(c, owner)
    };
    if has_index {
        let slot = castle_rt(c, gc.castle_id).guardian_id[guardian as usize];
        if slot != 0 {
            // Check if guardian already exists, refuse to spawn if so.
            let taken = unit::get(c, slot).map(|u| u.kind == crate::map::BL_MOB).unwrap_or(false) && get(c, slot).map(|g| g.number == guardian).unwrap_or(false);
            if taken {
                log::error!("mob_spawn_guardian: Attempted to spawn guardian in position {guardian} which already has a guardian (castle map {mapname})");
                return 0;
            }
        }
    }
    let data = crate::mob::spawn::SpawnArgs { id: mob_id, m, x, y, num: 1, name, eventname, ..Default::default() };
    let Some(md) = crate::mob::spawn::mob_spawn_dataset(c, &data, 0) else { return 0 };
    let mut gd = GuardianData { id: md.id, number: guardian, guild_id: owner, emblem_id: 0, guardup_lv: 0, guild_name: String::new(), castle_id: gc.castle_id };
    let mut rt = castle_rt(c, gc.castle_id);
    if has_index {
        // permanent guardian
        rt.guardian_id[guardian as usize] = md.id;
    } else {
        // temporary guardian
        match rt.temp_guardians.iter().position(|&t| t == 0) {
            Some(i) => rt.temp_guardians[i] = md.id,
            None => rt.temp_guardians.push(md.id),
        }
    }
    put_castle_rt(c, rt);
    let wait_for_guild = match &g {
        Some(g) => {
            gd.emblem_id = g.guild.emblem_id;
            gd.guild_name = name24(&g.guild.name);
            gd.guardup_lv = crate::guild::guild_checkskill(&g.guild, crate::db::skills::ids::GD_GUARDUP as i32);
            false
        }
        None => gd.guild_id != 0,
    };
    attach(c, &gd);
    if wait_for_guild {
        crate::timer::add(c, c.tick() + crate::conf::mob_respawn_time(), crate::timer::Func::MobSpawnGuardianSub, md.id, gd.guild_id as i64);
    }
    crate::mob::spawn::mob_spawn(c, md.id);
    md.id
}

/// mob.cpp mob_guardian_guildchange: the castle has a new owner. Without an owner the guardians go (their slot is
/// saved empty) and the Emperium only loses its guild data; with an owner that is not loaded the guardian goes too;
/// otherwise the guardian takes the new guild's data. Returns 1 when the guardian now serves the new guild.
pub fn mob_guardian_guildchange(c: &Ctx, id: u32) -> i32 {
    let Some(mut gd) = get(c, id) else { return 0 };
    let Some(md) = crate::mob::get(c, id) else { return 0 };
    let owner = castle_rt(c, gd.castle_id).data.guild_id;
    let in_slot = gd.number >= 0 && gd.number < MAX_GUARDIANS as i32;
    if owner == 0 {
        // Castle with no owner? Delete the guardians.
        if md.mob_id == MOBID_EMPERIUM as i32 {
            // But don't delete the emperium, just clear it's guild-data
            gd.guild_id = 0;
            gd.emblem_id = 0;
            gd.guild_name = String::new();
            put(c, &gd);
        } else {
            if in_slot && castle_rt(c, gd.castle_id).data.visible_g[gd.number as usize] {
                guild_castledatasave(c, gd.castle_id, CD_ENABLED_GUARDIAN00 + gd.number, 0);
            }
            crate::mob::unit::unit_free(c, id, unit::CLR_OUTSIGHT); // Remove guardian.
        }
        return 0;
    }
    let Some(g) = crate::guild::guild_search(c, owner) else {
        // Properly remove guardian info from Castle data.
        log::error!("mob_guardian_guildchange: New Guild (id {}) does not exists!", gd.guild_id);
        if in_slot {
            guild_castledatasave(c, gd.castle_id, CD_ENABLED_GUARDIAN00 + gd.number, 0);
        }
        crate::mob::unit::unit_free(c, id, unit::CLR_OUTSIGHT);
        return 0;
    };
    gd.guild_id = g.guild.guild_id;
    gd.emblem_id = g.guild.emblem_id;
    gd.guardup_lv = crate::guild::guild_checkskill(&g.guild, crate::db::skills::ids::GD_GUARDUP as i32);
    gd.guild_name = name24(&g.guild.name);
    put(c, &gd);
    1
}

// ------------------------------------------------------------------------------------------------ other files' hooks

/// mob.cpp mob_dead: a dying guardian's slot is saved as empty (`CD_ENABLED_GUARDIAN00 + number`, 0).
pub fn mob_dead(c: &Ctx, id: u32) {
    if let Some(gd) = get(c, id) {
        if gd.number >= 0 && gd.number < MAX_GUARDIANS as i32 {
            guild_castledatasave(c, gd.castle_id, CD_ENABLED_GUARDIAN00 + gd.number, 0);
        }
    }
}

/// unit.cpp unit_free for BL_MOB: a guardian leaves its castle slot (permanent: guardian[number].id = 0; otherwise its
/// temp_guardians entry, if any, becomes 0) and its guardian data is freed.
pub fn unit_free(c: &Ctx, id: u32) {
    let Some(gd) = get(c, id) else { return };
    let mut rt = castle_rt(c, gd.castle_id);
    if gd.number >= 0 && gd.number < MAX_GUARDIANS as i32 {
        rt.guardian_id[gd.number as usize] = 0;
    } else if let Some(i) = rt.temp_guardians.iter().position(|&t| t == id) {
        rt.temp_guardians[i] = 0;
    }
    put_castle_rt(c, rt);
    c.db().guardian_data().id().delete(id);
}

/// status.cpp status_calc_mob_'s `flag&4` part ("Strengthen Guardians - custom value +10% / lv"): the castle's defense
/// (RENEWAL: +50 Max HP per 5 defense, DEF / MDEF + (defense + 2) / 3 for the FE castles and the Emperium) and, for
/// guardians, +1000 Max HP per defense and GD_GUARDUP's ATK / ASPD. `m` is the monster's map (md->m).
///
/// Deliberately different: rAthena dereferences the missing castle of a guardian taken off its castle map (a
/// crash); the module logs the error and skips the guardian bonus.
pub fn status_calc_mob_castle(c: &Ctx, md: &crate::mob::MobData, m: u16, status: &mut StatusData) {
    let mapname = crate::data::maps().get(m).map(|d| d.name.clone()).unwrap_or_default();
    let emperium = md.mob_id == MOBID_EMPERIUM as i32;
    let Some(gc) = castle_db().mapname2gc(&mapname) else {
        log::error!("status_calc_mob: No castle set at map {mapname}");
        if !emperium {
            log::error!("status_calc_mob: guardian {} has no castle (rAthena dereferences a null castle here)", md.id);
        }
        return;
    };
    let defense = castle_rt(c, gc.castle_id).data.defense;
    if gc.castle_id < 24 || emperium {
        // RENEWAL
        status.max_hp = status.max_hp.wrapping_add((50 * (defense / 5)) as u32);
        status.hp = status.max_hp;
        status.def = (status.def as i32 + (defense + 2) / 3) as i16;
        status.mdef = (status.mdef as i32 + (defense + 2) / 3) as i16;
    }
    if !emperium {
        let lv = guardup_lv(c, md.id);
        status.max_hp = status.max_hp.wrapping_add((1000 * defense) as u32);
        status.hp = status.max_hp;
        status.batk = status.batk.wrapping_add(2 * lv + 8);
        status.rhw.atk = (status.rhw.atk as i32 + 2 * lv + 8) as u16;
        status.rhw.atk2 = (status.rhw.atk2 as i32 + 2 * lv + 8) as u16;
        status.aspd_rate = (status.aspd_rate as i32 - (2 * lv + 3)) as i16;
    }
}

/// clif.cpp clif_name for BL_MOB: a guardian with a guild answers ZC_ACK_REQNAMEALL with its guild's name and its
/// castle's name. Returns false when the monster is not such a guardian (the caller sends the normal answer).
pub fn clif_name(c: &Ctx, src: &Unit, bl: &Unit, target: crate::clif::Target) -> bool {
    let Some(gd) = get(c, bl.id) else { return false };
    if gd.guild_id == 0 {
        return false;
    }
    let md_name = crate::mob::get(c, bl.id).map(|m| m.name).unwrap_or_else(|| bl.name.clone());
    let castle_name = castle_db().find(gd.castle_id).map(|g| g.castle_name.clone()).unwrap_or_default();
    let p = crate::net::packets::PACKET_ZC_ACK_REQNAMEALL {
        packet_id: crate::net::packets::PACKET_ZC_ACK_REQNAMEALL::ID,
        gid: bl.id as i32,
        name: md_name,
        party_name: String::new(),
        guild_name: gd.guild_name.clone(),
        position_name: castle_name,
        title_id: 0,
    };
    crate::clif::send(c, &p.to_vec(), src, target);
    true
}
