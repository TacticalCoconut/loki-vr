//! map/instance.cpp: instances (memorial dungeons). An instance is created for an owner (a character, party, guild
//! or clan, or nobody), waits in the subscription queue until its maps are copied (instance_subscription_timer:
//! map_addinstancemap for the entry map and every additional map, then the NPC duplicates and their
//! OnInstanceInit), is entered with instance_enter, keeps its `'` variables, and is destroyed by its life timer, its
//! idle timer (nobody inside), a script, the owner's destroy button or the owner's end (party / guild broken).
//!
//! State (rAthena's globals and structs → rows):
//! - `instance_data` = `instances` (unordered_map instance id → s_instance_data), the maps a list of (m, src_m).
//! - `instance_state` = instance_wait (the subscription queue and its timer) and instance_count.
//! - sd->instance_id / sd->instance_mode = `pc_instance`; nd->instance_id = `npc_instance` (instance_npc.rs);
//!   party_data / MapGuild / clan instance_id are those systems' fields (party.rs, guild.rs, clan.rs).
//! - the regs (`'` variables) are the script engine's `instance_reg_num` / `_str` rows (script/reg.rs).
//! Maps: instance_map.rs; NPC duplicates: instance_npc.rs; packets: clif_instance.rs; script commands:
//! script/buildins/instance.rs.

use spacetimedb::{table, SpacetimeType, Table};

use crate::db::misc::instance::{instance_db, instance_search_db_name, InstanceDb};
use crate::timer::{self, Func};
use crate::world::Ctx;

/// instance.cpp INSTANCE_INTERVAL: the subscription queue's check interval (ms).
pub const INSTANCE_INTERVAL: i64 = 60000;

/// enum e_instance_state.
pub const INSTANCE_IDLE: u8 = 0;
pub const INSTANCE_BUSY: u8 = 1;

/// enum e_instance_mode.
pub const IM_NONE: u8 = 0;
pub const IM_CHAR: u8 = 1;
pub const IM_PARTY: u8 = 2;
pub const IM_GUILD: u8 = 3;
pub const IM_CLAN: u8 = 4;
pub const IM_MAX: u8 = 5;

/// enum e_instance_enter.
pub const IE_OK: i32 = 0;
pub const IE_NOMEMBER: i32 = 1;
pub const IE_NOINSTANCE: i32 = 2;
pub const IE_OTHER: i32 = 3;

/// enum e_instance_notify.
pub const IN_NOTIFY: u32 = 0;
pub const IN_DESTROY_LIVE_TIMEOUT: u32 = 1;
pub const IN_DESTROY_ENTER_TIMEOUT: u32 = 2;
pub const IN_DESTROY_USER_REQUEST: u32 = 3;
pub const IN_CREATE_FAIL: u32 = 4;

/// conf/battle/instance.conf / feature.conf values instance code reads (battle_config).
/// feature.instance_allow_reconnect: yes (PACKETVER >= 20120201 default on; conf/battle/feature.conf).
pub fn instance_allow_reconnect() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_INSTANCE_ALLOW_RECONNECT) != 0
}

/// instance.hpp struct s_instance_map.
#[derive(SpacetimeType, Clone, Copy, Debug, PartialEq, Eq)]
pub struct InstanceMapPair {
    pub m: u16,
    pub src_m: u16,
}

/// instance.hpp struct s_instance_data (the regs are the script engine's `instance_reg_*` rows).
#[table(accessor = instance_data)]
#[derive(Clone, Debug)]
pub struct InstanceData {
    /// The key of `instances` (the instance id).
    #[primary_key]
    pub instance_id: i32,
    /// Instance DB ID.
    pub id: i32,
    /// e_instance_state.
    pub state: u8,
    /// e_instance_mode.
    pub mode: u8,
    pub owner_id: i32,
    /// Life time of instance (time(nullptr) seconds).
    pub keep_limit: i64,
    /// Life time timer (0 = INVALID_TIMER).
    pub keep_timer: u64,
    /// Idle time of instance.
    pub idle_limit: i64,
    pub idle_timer: u64,
    pub nonpc: bool,
    pub nomapflag: bool,
    /// Array of maps in instance.
    pub map: Vec<InstanceMapPair>,
}

/// instance_wait (the subscription queue) and instance_count.
#[table(accessor = instance_state)]
#[derive(Clone, Debug)]
pub struct InstanceState {
    #[primary_key]
    pub key: u8,
    /// Total created instances + 1 (int32; the next instance id).
    pub instance_count: i32,
    /// instance_wait.id (a deque: front first).
    pub wait_ids: Vec<i32>,
    /// instance_wait.timer (0 = INVALID_TIMER).
    pub wait_timer: u64,
}

/// map_session_data's instance fields.
#[table(accessor = pc_instance)]
#[derive(Clone, Debug)]
pub struct PcInstance {
    #[primary_key]
    pub account_id: u32,
    /// sd->instance_id (the IM_CHAR instance).
    pub instance_id: i32,
    /// sd->instance_mode: the mode of the instance the player last left (the destroy button's checks).
    pub instance_mode: u8,
}

// ---------------------------------------------------------------------------------------------- rows

/// util::umap_find(instances, instance_id).
pub fn find(c: &Ctx, instance_id: i32) -> Option<InstanceData> {
    c.db().instance_data().instance_id().find(instance_id)
}

fn put(c: &Ctx, d: &InstanceData) {
    if c.db().instance_data().instance_id().find(d.instance_id).is_some() {
        c.db().instance_data().instance_id().update(d.clone());
    } else {
        c.db().instance_data().insert(d.clone());
    }
}

fn state(c: &Ctx) -> InstanceState {
    c.db().instance_state().key().find(0).unwrap_or(InstanceState { key: 0, instance_count: 1, wait_ids: Vec::new(), wait_timer: 0 })
}

fn put_state(c: &Ctx, s: &InstanceState) {
    if c.db().instance_state().key().find(0).is_some() {
        c.db().instance_state().key().update(s.clone());
    } else {
        c.db().instance_state().insert(s.clone());
    }
}

/// The `instances` keys (iteration order: by id; rAthena's unordered_map order is not observable here, see pc_login).
pub fn all_ids(c: &Ctx) -> Vec<i32> {
    let mut v: Vec<i32> = c.db().instance_data().iter().map(|d| d.instance_id).collect();
    v.sort_unstable();
    v
}

fn pcx(c: &Ctx, aid: u32) -> PcInstance {
    c.db().pc_instance().account_id().find(aid).unwrap_or(PcInstance { account_id: aid, instance_id: 0, instance_mode: 0 })
}

fn put_pcx(c: &Ctx, x: &PcInstance) {
    if c.db().pc_instance().account_id().find(x.account_id).is_some() {
        c.db().pc_instance().account_id().update(x.clone());
    } else {
        c.db().pc_instance().insert(x.clone());
    }
}

/// sd->instance_id.
pub fn sd_instance_id(c: &Ctx, aid: u32) -> i32 {
    c.db().pc_instance().account_id().find(aid).map(|x| x.instance_id).unwrap_or(0)
}

fn set_sd_instance_id(c: &Ctx, aid: u32, instance_id: i32) {
    let mut x = pcx(c, aid);
    x.instance_id = instance_id;
    put_pcx(c, &x);
}

/// sd->instance_mode.
pub fn sd_instance_mode(c: &Ctx, aid: u32) -> u8 {
    c.db().pc_instance().account_id().find(aid).map(|x| x.instance_mode).unwrap_or(0)
}

fn set_sd_instance_mode(c: &Ctx, aid: u32, mode: u8) {
    let mut x = pcx(c, aid);
    x.instance_mode = mode;
    put_pcx(c, &x);
}

/// The party's instance id (party_data->instance_id).
fn party_instance_id(c: &Ctx, party_id: i32) -> Option<i32> {
    crate::party::party_search(c, party_id as u32).map(|p| p.instance_id)
}

/// The guild's instance id (MapGuild::instance_id).
fn guild_instance_id(c: &Ctx, guild_id: i32) -> Option<i32> {
    crate::guild::guild_search(c, guild_id).map(|g| g.instance_id)
}

fn set_guild_instance_id(c: &Ctx, guild_id: i32, instance_id: i32) {
    if let Some(mut g) = crate::guild::guild_search(c, guild_id) {
        g.instance_id = instance_id;
        crate::guild::put_guild(c, &g);
    }
}

/// The clan's instance id: struct clan's `uint16 instance_id` (mmo.hpp), None when clan_search fails.
fn clan_instance_id(c: &Ctx, clan_id: i32) -> Option<i32> {
    crate::clan::clan_search(clan_id).map(|_| crate::clan::instance_id(c, clan_id) as i32)
}

/// The character's status ids: sd->status.char_id / party_id / guild_id / clan_id.
fn status_ids(c: &Ctx, aid: u32) -> (i32, i32, i32, i32) {
    match crate::pc::character_of(c, aid) {
        Some(ch) => (ch.id as i32, ch.party_id as i32, ch.guild_id as i32, ch.clan_id as i32),
        None => (0, 0, 0, 0),
    }
}

/// map.cpp map_charid2sd: the player of a character id — only once it is in charid_db, which map_addiddb fills in
/// pc_reg_received (the end of the login: `pc.active`). A player logging in is not found yet (rAthena's pc_authok ->
/// pc_setpos -> instance_addusers -> instance_stopidletimer then tells nobody).
fn charid2sd(c: &Ctx, char_id: u32) -> Option<u32> {
    let u = crate::pc::charid2sd(c, char_id)?;
    crate::pc::get(c, u.id).filter(|p| p.active).map(|_| u.id)
}

// ---------------------------------------------------------------------------------------------- instance.cpp

/// instance.cpp instance_search_db_name.
pub fn search_db_name(name: &str) -> Option<&'static InstanceDb> {
    instance_search_db_name(name)
}

/// Who the instance packets go to (instance_getsd's sd and send_target).
pub enum Recipient {
    /// SELF.
    One(u32),
    /// PARTY: the party of this member.
    Party(u32),
    /// GUILD: the guild of this member.
    Guild(u32),
    /// CLAN: the clan of this member.
    Clan(u32),
}

/// instance.cpp instance_getsd: an online player of the owner and the send target (None = no sd: nothing is sent).
pub fn instance_getsd(c: &Ctx, instance_id: i32) -> Option<Recipient> {
    let idata = find(c, instance_id)?;
    match idata.mode {
        IM_NONE => None,
        IM_GUILD => {
            // guild_getavailablesd(guild_search(idata->owner_id)->guild) (a missing guild would crash rAthena)
            let g = crate::guild::guild_search(c, idata.owner_id)?;
            crate::guild::guild_getavailablesd(&g.guild).map(Recipient::Guild)
        }
        IM_PARTY => {
            let p = crate::party::party_search(c, idata.owner_id as u32)?;
            crate::party::party_getavailablesd(&p).map(Recipient::Party)
        }
        IM_CHAR => charid2sd(c, idata.owner_id as u32).map(Recipient::One),
        IM_CLAN => {
            // clan_getavailablesd(*clan_search(idata->owner_id))
            crate::clan::clan_search(idata.owner_id)?;
            crate::clan::clan_getavailablesd(c, idata.owner_id).map(Recipient::Clan)
        }
        _ => None,
    }
}

/// instance.cpp instance_delete_timer: destroys the instance (its keep or idle timer ran out).
pub fn instance_delete_timer(c: &Ctx, id: u32) {
    instance_destroy(c, id as i32);
}

/// Does the owner of this mode still exist (the `(sd = map_charid2sd(..))` / party_search / guild_search /
/// clan_search tests of instance.cpp)?
fn owner_exists(c: &Ctx, mode: u8, owner_id: i32) -> bool {
    match mode {
        IM_CHAR => charid2sd(c, owner_id as u32).is_some(),
        IM_PARTY => crate::party::party_search(c, owner_id as u32).is_some(),
        IM_GUILD => crate::guild::guild_search(c, owner_id).is_some(),
        IM_CLAN => crate::clan::clan_search(owner_id).is_some(),
        _ => false,
    }
}

/// instance.cpp instance_subscription_timer: the first instance of the queue gets its maps (instance_addmap); the
/// others are told their new standby position.
pub fn instance_subscription_timer(c: &Ctx) {
    let s = state(c);
    let Some(&instance_id) = s.wait_ids.first() else { return };
    if instance_id <= 0 {
        return;
    }
    let Some(idata) = find(c, instance_id) else { return };
    let mode = idata.mode;
    // Check that maps have been added
    let ret = instance_addmap(c, instance_id);
    let owner = match mode {
        IM_NONE => false,
        IM_CHAR | IM_PARTY | IM_GUILD | IM_CLAN => {
            // If no maps are created, tell the owner to wait
            let exists = ret == 0 && owner_exists(c, mode, idata.owner_id);
            if exists {
                crate::clif_instance::clif_instance_changewait(c, instance_id, 0xffff);
            }
            exists
        }
        _ => return,
    };
    let mut s = state(c);
    if !s.wait_ids.is_empty() {
        s.wait_ids.remove(0);
    }
    put_state(c, &s);
    let idata_state = find(c, instance_id).map(|d| d.state).unwrap_or(INSTANCE_BUSY);
    for i in 0..s.wait_ids.len() {
        // (rAthena tells the instance it just handled, not the one at position i)
        if idata_state == INSTANCE_IDLE && owner {
            crate::clif_instance::clif_instance_changewait(c, instance_id, i as i32 + 1);
        }
    }
    let mut s = state(c);
    s.wait_timer = if !s.wait_ids.is_empty() { timer::add(c, c.tick() + INSTANCE_INTERVAL, Func::InstanceSubscription, 0, 0) } else { 0 };
    put_state(c, &s);
}

/// instance.cpp instance_startkeeptimer: the life timer (and its notice) unless the instance has one or is infinite.
pub fn instance_startkeeptimer(c: &Ctx, instance_id: i32) -> bool {
    // No timer
    let Some(mut idata) = find(c, instance_id) else { return false };
    if idata.keep_timer != 0 {
        return false;
    }
    let Some(db) = instance_db().find(idata.id) else { return false };
    // Infinite duration instance
    if db.infinite_limit {
        return true;
    }
    // Add timer
    idata.keep_limit = c.tick() / 1000 + db.limit;
    idata.keep_timer = timer::add(c, c.tick() + db.limit * 1000, Func::InstanceDelete, instance_id as u32, 0);
    put(c, &idata);
    notify_status(c, &idata)
}

/// The `switch(idata->mode)` of instance_startkeeptimer / instance_startidletimer / instance_addmap:
/// clif_instance_status when the owner exists. False for an unknown mode.
fn notify_status(c: &Ctx, idata: &InstanceData) -> bool {
    match idata.mode {
        IM_NONE => true,
        IM_CHAR | IM_PARTY | IM_GUILD | IM_CLAN => {
            if owner_exists(c, idata.mode, idata.owner_id) {
                crate::clif_instance::clif_instance_status(c, idata.instance_id, idata.keep_limit as u32, idata.idle_limit as u32);
            }
            true
        }
        _ => false,
    }
}

/// instance.cpp instance_startidletimer: the idle timer (default 5 minutes) and its notice.
pub fn instance_startidletimer(c: &Ctx, instance_id: i32) -> bool {
    // No current timer
    let Some(mut idata) = find(c, instance_id) else { return false };
    if idata.idle_timer != 0 {
        return false;
    }
    let Some(db) = instance_db().find(idata.id) else { return false };
    // Infinite idle duration instance
    if db.infinite_timeout {
        return true;
    }
    // Add the timer
    idata.idle_limit = c.tick() / 1000 + db.timeout;
    idata.idle_timer = timer::add(c, c.tick() + db.timeout * 1000, Func::InstanceDelete, instance_id as u32, 0);
    put(c, &idata);
    notify_status(c, &idata)
}

/// instance.cpp instance_stopidletimer: somebody is back in the instance.
pub fn instance_stopidletimer(c: &Ctx, instance_id: i32) -> bool {
    // No timer
    let Some(mut idata) = find(c, instance_id) else { return false };
    if idata.idle_timer == 0 {
        return false;
    }
    // Delete the timer - Party has returned or instance is destroyed
    idata.idle_limit = 0;
    timer::delete(c, idata.idle_timer);
    idata.idle_timer = 0;
    put(c, &idata);
    match idata.mode {
        IM_NONE => true,
        IM_CHAR | IM_PARTY | IM_GUILD | IM_CLAN => {
            if owner_exists(c, idata.mode, idata.owner_id) {
                crate::clif_instance::clif_instance_changestatus(c, instance_id, IN_NOTIFY, idata.idle_limit as u32);
            }
            true
        }
        _ => false,
    }
}

/// instance.cpp instance_addnpc: the NPCs of every source map are duplicated on its copy (npc_duplicate4instance, in
/// the source map's block order), then every NPC of the copies runs its OnInstanceInit (npc_instanceinit).
pub fn instance_addnpc(c: &Ctx, instance_id: i32) {
    let Some(idata) = find(c, instance_id) else { return };
    // First add the NPCs (ev_db's iteration ranks are brought up to date once, after the last copy: nothing iterates
    // ev_db meanwhile)
    crate::script::npc_run::ev_db_lock();
    for it in &idata.map {
        let Some(md) = crate::data::maps().get(it.m) else { continue };
        for nd in crate::map::units_in_area(c, it.src_m, 0, 0, md.xs, md.ys, crate::map::BL_NPC) {
            crate::instance_npc::npc_duplicate4instance(c, nd.id, it.m);
        }
    }
    crate::script::npc_run::ev_db_unlock(c);
    // Now run their OnInstanceInit
    for it in &idata.map {
        let Some(md) = crate::data::maps().get(it.m) else { continue };
        for nd in crate::map::units_in_area(c, it.m, 0, 0, md.xs, md.ys, crate::map::BL_NPC) {
            if crate::unit::get(c, nd.id).map(|u| u.on_map).unwrap_or(false) {
                crate::instance_npc::npc_instanceinit(c, nd.id);
            }
        }
    }
}

/// instance.cpp instance_create: -4 no free instance id, -3 the owner already has an instance, -2 owner not found
/// (or an unknown mode), -1 unknown instance name; otherwise the new instance id.
pub fn instance_create(c: &Ctx, owner_id: i32, name: &str, mode: i32) -> i32 {
    let Some(db) = search_db_name(name) else {
        log::error!("instance_create: Unknown instance {name} creation was attempted.");
        return -1;
    };
    let mut sd: Option<u32> = None;
    match mode {
        m if m == IM_NONE as i32 => {}
        m if m == IM_CHAR as i32 => {
            let Some(aid) = charid2sd(c, owner_id as u32) else {
                log::error!("instance_create: Character {owner_id} not found for instance '{name}'.");
                return -2;
            };
            if sd_instance_id(c, aid) > 0 {
                return -3; // Player already instancing
            }
            sd = Some(aid);
        }
        m if m == IM_PARTY as i32 => {
            let Some(iid) = party_instance_id(c, owner_id) else {
                log::error!("instance_create: Party {owner_id} not found for instance '{name}'.");
                return -2;
            };
            if iid > 0 {
                return -3; // Party already instancing
            }
        }
        m if m == IM_GUILD as i32 => {
            let Some(iid) = guild_instance_id(c, owner_id) else {
                log::error!("instance_create: Guild {owner_id} not found for instance '{name}'.");
                return -2;
            };
            if iid > 0 {
                return -3; // Guild already instancing
            }
        }
        m if m == IM_CLAN as i32 => {
            let Some(iid) = clan_instance_id(c, owner_id) else {
                log::error!("instance_create: Clan {owner_id} not found for instance '{name}'.");
                return -2;
            };
            if iid > 0 {
                return -3; // Clan already instancing
            }
        }
        _ => {
            log::error!("instance_create: Unknown mode {} for owner_id {owner_id} and name {name}.", mode as u8);
            return -2;
        }
    }
    let mut s = state(c);
    if s.instance_count <= 0 {
        return -4;
    }
    let instance_id = s.instance_count;
    s.instance_count = s.instance_count.wrapping_add(1);
    put_state(c, &s);
    let mode = mode as u8;
    let entry = InstanceData {
        instance_id,
        id: db.id,
        state: INSTANCE_IDLE,
        mode,
        owner_id,
        keep_limit: 0,
        keep_timer: 0,
        idle_limit: 0,
        idle_timer: 0,
        nonpc: false,
        nomapflag: false,
        map: Vec::new(),
    };
    put(c, &entry);
    match mode {
        IM_CHAR => {
            if let Some(aid) = sd {
                set_sd_instance_id(c, aid, instance_id);
            }
        }
        IM_PARTY => {
            crate::party::set_instance_id(c, owner_id as u32, instance_id);
            if let Some(p) = crate::party::party_search(c, owner_id as u32) {
                // ARR_FIND(0, MAX_PARTY, i, pd->party.member[i].leader)
                if let Some(m) = p.party.member.iter().take(crate::party::MAX_PARTY).find(|m| m.leader) {
                    sd = charid2sd(c, m.char_id);
                }
            }
        }
        IM_GUILD => {
            set_guild_instance_id(c, owner_id, instance_id);
            if let Some(g) = crate::guild::guild_search(c, owner_id) {
                sd = g.guild.member.first().and_then(|m| charid2sd(c, m.char_id));
            }
        }
        IM_CLAN => {
            // cd->instance_id is a uint16
            crate::clan::set_instance_id(c, owner_id, instance_id as u16);
        }
        _ => {}
    }
    if let Some(aid) = sd {
        set_sd_instance_mode(c, aid, mode);
    }
    let mut s = state(c);
    s.wait_ids.push(instance_id);
    let size = s.wait_ids.len();
    put_state(c, &s);
    crate::clif_instance::clif_instance_create(c, instance_id, size);
    instance_subscription_timer(c);
    log::info!("[Instance] Created: {name} ({instance_id})");
    // Start the instance timer on instance creation
    instance_startkeeptimer(c, instance_id);
    instance_id
}

/// instance.cpp instance_addmap: the instance becomes busy (its idle timer starts), its maps are copied (entry map,
/// then the additional maps) and its NPCs duplicated. 0 on failure, else the number of maps.
pub fn instance_addmap(c: &Ctx, instance_id: i32) -> usize {
    if instance_id <= 0 {
        return 0;
    }
    let Some(mut idata) = find(c, instance_id) else { return 0 };
    // If the instance isn't idle, we can't do anything
    if idata.state != INSTANCE_IDLE {
        return 0;
    }
    let Some(db) = instance_db().find(idata.id) else { return 0 };
    // Set to busy, update timers
    idata.state = INSTANCE_BUSY;
    if !db.infinite_timeout {
        idata.idle_limit = c.tick() / 1000 + db.timeout;
        idata.idle_timer = timer::add(c, c.tick() + db.timeout * 1000, Func::InstanceDelete, instance_id as u32, 0);
    }
    idata.nomapflag = db.nomapflag;
    idata.nonpc = db.nonpc;
    put(c, &idata);
    // Add initial map
    let m = crate::instance_map::map_addinstancemap(c, db.enter.map as i32, instance_id, db.nomapflag);
    if m < 0 {
        log::error!("instance_addmap: Failed to create initial map for instance '{}' ({instance_id}).", db.name);
        return 0;
    }
    let Some(mut idata) = find(c, instance_id) else { return 0 };
    idata.map.push(InstanceMapPair { m: m as u16, src_m: db.enter.map as u16 });
    put(c, &idata);
    // Add extra maps (if any)
    for &it in &db.maplist {
        let m = crate::instance_map::map_addinstancemap(c, it as i32, instance_id, db.nomapflag);
        if m < 0 {
            // An error occurred adding a map
            log::error!("instance_addmap: No maps added to instance '{}' ({instance_id}).", db.name);
            return 0;
        }
        let Some(mut idata) = find(c, instance_id) else { return 0 };
        idata.map.push(InstanceMapPair { m: m as u16, src_m: it as u16 });
        put(c, &idata);
    }
    // Create NPCs on all maps
    if !db.nonpc {
        instance_addnpc(c, instance_id);
    }
    let Some(idata) = find(c, instance_id) else { return 0 };
    // Inform the owner of the created instance
    if !notify_status(c, &idata) {
        return 0;
    }
    idata.map.len()
}

/// instance.cpp instance_mapid: the copy of map `m` in this (busy) instance, `m` itself when the instance has no copy
/// of it, -1 for an unknown or idle instance. (Its "Map ID %d does not exist" error never fires: map_mapid2mapname
/// never returns nullptr — "" for an unknown map, "Floating" for -1 — so an unknown `m` comes back as it is.)
pub fn instance_mapid(c: &Ctx, m: i32, instance_id: i32) -> i32 {
    let Some(idata) = find(c, instance_id) else { return -1 };
    if idata.state != INSTANCE_BUSY {
        return -1;
    }
    for it in &idata.map {
        if it.src_m as i32 == m {
            let alt_name = crate::instance_map::instance_generate_mapname(m, instance_id);
            return crate::data::maps().id_of(&alt_name).map(|x| x as i32).unwrap_or(-1);
        }
    }
    m
}

/// instance.cpp instance_destroy_command: the destroy button of the instance window (clif_parse_MemorialDungeonCommand):
/// the owner (the character, the party leader, the guild master) destroys a destroyable instance.
pub fn instance_destroy_command(c: &Ctx, aid: u32) {
    let (char_id, party_id, guild_id, _) = status_ids(c, aid);
    let _ = char_id;
    let mode = sd_instance_mode(c, aid);
    let sd_iid = sd_instance_id(c, aid);
    let mut instance_id = 0;
    let mut idata = None;
    if mode == IM_CHAR && sd_iid > 0 {
        idata = find(c, sd_iid);
        if idata.is_none() {
            return;
        }
        instance_id = sd_iid;
    } else if mode == IM_PARTY && party_id > 0 {
        let Some(pd) = crate::party::party_search(c, party_id as u32) else { return };
        idata = find(c, pd.instance_id);
        if idata.is_none() {
            return;
        }
        // ARR_FIND(0, MAX_PARTY, i, pd->data[i].sd == sd && pd->party.member[i].leader)
        let leader = (0..crate::party::MAX_PARTY).any(|i| pd.data.get(i).map(|d| d.sd == aid).unwrap_or(false) && pd.party.member.get(i).map(|m| m.leader).unwrap_or(false));
        if !leader {
            return; // Player is not party leader
        }
        instance_id = pd.instance_id;
    } else if mode == IM_GUILD && crate::guild::sd_guild(c, aid).map(|g| g.instance_id > 0).unwrap_or(false) {
        let Some(gd) = crate::guild::guild_search(c, guild_id) else { return };
        idata = find(c, gd.instance_id);
        if idata.is_none() {
            return;
        }
        let name = crate::pc::character_of(c, aid).map(|ch| ch.name).unwrap_or_default();
        if name != gd.guild.master {
            return; // Player is not guild master
        }
        instance_id = gd.instance_id;
    }
    if instance_id == 0 {
        return; // Checks above failed
    }
    let Some(idata) = idata else { return };
    if !instance_db().find(idata.id).map(|db| db.destroyable).unwrap_or(false) {
        return; // Instance is flagged as non-destroyable
    }
    instance_destroy(c, instance_id);
    // Check for any other active instances and display their info
    let sd_iid = sd_instance_id(c, aid);
    if sd_iid > 0 {
        instance_reqinfo(c, aid, sd_iid);
    }
    let (_, party_id, guild_id, _) = status_ids(c, aid);
    if party_id > 0 {
        let Some(pd) = crate::party::party_search(c, party_id as u32) else { return };
        if pd.instance_id > 0 {
            instance_reqinfo(c, aid, pd.instance_id);
        }
    }
    if crate::guild::sd_guild(c, aid).map(|g| g.instance_id > 0).unwrap_or(false) {
        let Some(gd) = crate::guild::guild_search(c, guild_id) else { return };
        instance_reqinfo(c, aid, gd.instance_id);
    }
}

/// instance.cpp instance_destroy: removes an instance, its maps (players go to their save points) and NPCs (after
/// their OnInstanceDestroy), its timers and variables, and tells the owner why.
pub fn instance_destroy(c: &Ctx, instance_id: i32) -> bool {
    let Some(idata) = find(c, instance_id) else { return false };
    let mode = idata.mode;
    let mut kind = IN_NOTIFY;
    // the owner (sd / pd / gd / cd) as it is now
    let owner = owner_exists(c, mode, idata.owner_id);
    let owner_sd = if mode == IM_CHAR { charid2sd(c, idata.owner_id as u32) } else { None };
    if idata.state == INSTANCE_IDLE {
        let mut s = state(c);
        if let Some(pos) = s.wait_ids.iter().position(|&id| id == instance_id) {
            s.wait_ids.remove(pos);
            put_state(c, &s);
            for i in 0..s.wait_ids.len() {
                if find(c, s.wait_ids[i]).map(|d| d.state == INSTANCE_IDLE).unwrap_or(false) && owner {
                    crate::clif_instance::clif_instance_changewait(c, instance_id, i as i32 + 1);
                }
            }
            // (the pending subscription timer is not deleted: a new one is added beside it)
            let mut s = state(c);
            s.wait_timer = if !s.wait_ids.is_empty() { timer::add(c, c.tick() + INSTANCE_INTERVAL, Func::InstanceSubscription, 0, 0) } else { 0 };
            put_state(c, &s);
        }
    } else {
        let now = c.tick() / 1000;
        kind = if idata.keep_limit != 0 && idata.keep_limit <= now {
            IN_DESTROY_LIVE_TIMEOUT
        } else if idata.idle_limit != 0 && idata.idle_limit <= now {
            IN_DESTROY_ENTER_TIMEOUT
        } else {
            IN_DESTROY_USER_REQUEST
        };
        // Run OnInstanceDestroy on all NPCs in the instance
        for it in &idata.map {
            if let Some(md) = crate::data::maps().get(it.m) {
                for nd in crate::map::units_in_area(c, it.m, 0, 0, md.xs, md.ys, crate::map::BL_NPC) {
                    if crate::unit::get(c, nd.id).map(|u| u.on_map).unwrap_or(false) {
                        crate::instance_npc::npc_instancedestroy(c, nd.id);
                    }
                }
            }
            // (ev_db's ranks are brought up to date once the map's NPCs are unloaded)
            crate::script::npc_run::ev_db_lock();
            crate::instance_map::map_delinstancemap(c, it.m);
            crate::script::npc_run::ev_db_unlock(c);
        }
    }
    let Some(mut idata) = find(c, instance_id) else { return true };
    if idata.keep_timer != 0 {
        timer::delete(c, idata.keep_timer);
        idata.keep_timer = 0;
    }
    if idata.idle_timer != 0 {
        timer::delete(c, idata.idle_timer);
        idata.idle_timer = 0;
    }
    put(c, &idata);
    match mode {
        IM_CHAR => {
            if let Some(aid) = owner_sd {
                set_sd_instance_id(c, aid, 0);
            }
        }
        IM_PARTY if owner => crate::party::set_instance_id(c, idata.owner_id as u32, 0),
        IM_GUILD if owner => set_guild_instance_id(c, idata.owner_id, 0),
        IM_CLAN if owner => crate::clan::set_instance_id(c, idata.owner_id, 0),
        _ => {}
    }
    if mode != IM_NONE {
        if kind != IN_NOTIFY {
            crate::clif_instance::clif_instance_changestatus(c, instance_id, kind, 0);
        } else {
            crate::clif_instance::clif_instance_changewait(c, instance_id, 0xffff);
        }
    }
    // the instance's variables (idata->regs)
    crate::script::reg::clear_instance_regs(c, instance_id);
    let name = instance_db().find(idata.id).map(|d| d.name.clone()).unwrap_or_default();
    log::info!("[Instance] Destroyed: {name} ({instance_id})");
    c.db().instance_data().instance_id().delete(instance_id);
    true
}

/// instance.cpp instance_enter: warps a player into an instance's entry map (the database's entry point unless both
/// coordinates are given; they are int16 there, callers truncate). Returns an e_instance_enter value.
pub fn instance_enter(c: &Ctx, aid: u32, instance_id: i32, name: &str, x: i16, y: i16) -> i32 {
    let Some(db) = search_db_name(name) else {
        log::error!("instance_enter: Unknown instance \"{name}\".");
        return IE_OTHER;
    };
    // If one of the two coordinates was not given or is below zero, we use the entry point from the database
    let (x, y) = if x < 0 || y < 0 { (db.enter.x, db.enter.y) } else { (x, y) };
    let idata;
    let mode;
    if instance_id <= 0 {
        // Default party checks will be used
        mode = IM_PARTY;
        idata = None;
    } else {
        let Some(d) = find(c, instance_id) else { return IE_NOINSTANCE };
        mode = d.mode;
        idata = Some(d);
    }
    let (char_id, party_id, guild_id, clan_id) = status_ids(c, aid);
    match mode {
        IM_NONE => {}
        IM_CHAR => {
            if sd_instance_id(c, aid) == 0 {
                return IE_NOINSTANCE; // Player must have an instance
            }
            if idata.as_ref().map(|d| d.owner_id != char_id).unwrap_or(true) {
                return IE_OTHER;
            }
        }
        IM_PARTY => {
            if party_id == 0 {
                return IE_NOMEMBER; // Character must be in instance party
            }
            let Some(pd) = crate::party::party_search(c, party_id as u32) else { return IE_NOMEMBER };
            if pd.instance_id == 0 || idata.is_none() {
                return IE_NOINSTANCE; // Party must have an instance
            }
            if idata.as_ref().map(|d| d.owner_id != pd.party_id as i32).unwrap_or(true) {
                return IE_OTHER;
            }
        }
        IM_GUILD => {
            if guild_id == 0 {
                return IE_NOMEMBER; // Character must be in instance guild
            }
            let Some(gd) = crate::guild::guild_search(c, guild_id) else { return IE_NOMEMBER };
            if gd.instance_id == 0 {
                return IE_NOINSTANCE; // Guild must have an instance
            }
            if idata.as_ref().map(|d| d.owner_id != gd.guild_id as i32).unwrap_or(true) {
                return IE_OTHER;
            }
        }
        IM_CLAN => {
            if clan_id == 0 {
                return IE_NOMEMBER; // Character must be in instance clan
            }
            let Some(ciid) = clan_instance_id(c, clan_id) else { return IE_NOMEMBER };
            if ciid == 0 {
                return IE_NOINSTANCE; // Clan must have an instance
            }
            if idata.as_ref().map(|d| d.owner_id != clan_id).unwrap_or(true) {
                return IE_OTHER;
            }
        }
        _ => {}
    }
    let Some(idata) = idata else { return IE_OTHER };
    if idata.state != INSTANCE_BUSY {
        return IE_OTHER;
    }
    if idata.id != db.id {
        return IE_OTHER;
    }
    // Does the instance match?
    let m = instance_mapid(c, db.enter.map as i32, instance_id);
    if m < 0 {
        return IE_OTHER;
    }
    if crate::pc::setpos(c, aid, m as u16, x, y, crate::unit::CLR_OUTSIGHT) != crate::pc::SetPos::Ok {
        return IE_OTHER;
    }
    IE_OK
}

/// instance.cpp instance_reqinfo: the instance window for the owner — its standby position while it waits, its
/// timers once busy (when the player is not inside another instance). Sets sd->instance_mode.
pub fn instance_reqinfo(c: &Ctx, aid: u32, instance_id: i32) -> bool {
    let Some(idata) = find(c, instance_id) else { return false };
    if instance_db().find(idata.id).is_none() {
        return false;
    }
    if idata.state == INSTANCE_IDLE {
        // Say it's created if instance is not busy
        let s = state(c);
        if let Some(i) = s.wait_ids.iter().position(|&id| id == instance_id) {
            crate::clif_instance::clif_instance_create(c, instance_id, i + 1);
            set_sd_instance_mode(c, aid, idata.mode);
        }
    } else if idata.state == INSTANCE_BUSY {
        // Give info on the instance if busy
        let m = crate::unit::get(c, aid).map(|u| u.map).unwrap_or(0);
        let map_instance_id = crate::instance_map::map_instance_id(c, m);
        if map_instance_id == 0 || map_instance_id == instance_id {
            crate::clif_instance::clif_instance_status(c, instance_id, idata.keep_limit as u32, idata.idle_limit as u32);
            set_sd_instance_mode(c, aid, idata.mode);
        }
    }
    true
}

/// instance.cpp instance_addusers: a player entered one of the instance's maps (the idle timer stops, the keep timer
/// starts if it has none).
pub fn instance_addusers(c: &Ctx, instance_id: i32) -> bool {
    let Some(idata) = find(c, instance_id) else { return false };
    if idata.state != INSTANCE_BUSY {
        return false;
    }
    // Stop the idle timer if we had one
    instance_stopidletimer(c, instance_id);
    // Start the instance keep timer
    instance_startkeeptimer(c, instance_id);
    true
}

/// instance.cpp instance_delusers: a player is leaving one of the instance's maps; when it was the last one the idle
/// timer starts.
pub fn instance_delusers(c: &Ctx, instance_id: i32) -> bool {
    let Some(idata) = find(c, instance_id) else { return false };
    if idata.state != INSTANCE_BUSY {
        return false;
    }
    // If no one is in the instance, start the idle timer
    let users: i32 = idata.map.iter().map(|it| crate::mob::map_users(c, it.m).max(0)).sum();
    // We check the actual map.users before being updated, hence the 1
    // The instance should be empty if users are now <= 1
    if users <= 1 {
        instance_startidletimer(c, instance_id);
    }
    true
}

/// instance.cpp do_reload_instance (npc_reload, @reloadscript): the NPCs of every instance are duplicated again, the
/// keep timers restart, and the owners' players inside go back to the entry point (message 515). The module has no
/// @reloadscript (a changed world is a new publish: npc.rs build_world), so nothing calls this yet.
pub fn do_reload_instance(c: &Ctx) {
    for instance_id in all_ids(c) {
        let Some(idata) = find(c, instance_id) else { continue };
        if idata.map.is_empty() {
            continue;
        }
        // First we load the NPCs again
        if !idata.nonpc {
            instance_addnpc(c, instance_id);
        }
        // Create new keep timer
        if let Some(db) = instance_db().find(idata.id) {
            let Some(mut idata) = find(c, instance_id) else { continue };
            // Save the expire time
            idata.keep_limit = c.tick() / 1000 + db.limit;
            // Recreate a timer and save the associated timer ID
            if idata.keep_timer != 0 {
                timer::delete(c, idata.keep_timer);
            }
            idata.keep_timer = timer::add(c, c.tick() + db.limit * 1000, Func::InstanceDelete, instance_id as u32, 0);
            put(c, &idata);
        }
    }
    // Reset player to instance beginning (mapit_getallusers: pc_db order)
    let mut players: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    players.sort_by_key(|&a| crate::party::idb_order(a));
    for aid in players {
        let Some(u) = crate::unit::get(c, aid) else { continue };
        let map_iid = crate::instance_map::map_instance_id(c, u.map);
        if map_iid <= 0 {
            continue;
        }
        let Some(idata) = find(c, map_iid) else { continue };
        let db = instance_db().find(idata.id);
        let (_, party_id, guild_id, clan_id) = status_ids(c, aid);
        let instance_id = match idata.mode {
            IM_NONE => continue,
            IM_CHAR => {
                if sd_instance_id(c, aid) != map_iid {
                    continue; // Someone who is not instance owner is on instance map
                }
                sd_instance_id(c, aid)
            }
            IM_PARTY => match party_instance_id(c, party_id) {
                Some(i) if i == map_iid => i,
                _ => continue, // Someone not in party is on instance map
            },
            IM_GUILD => match guild_instance_id(c, guild_id) {
                Some(i) if i == map_iid => i,
                _ => continue, // Someone not in guild is on instance map
            },
            IM_CLAN => match clan_instance_id(c, clan_id) {
                Some(i) if i == map_iid => i,
                _ => continue, // Someone not in clan is on instance map
            },
            m => {
                log::error!("do_reload_instance: Unexpected instance mode for instance {} (id={map_iid}, mode={m}).", db.map(|d| d.name.as_str()).unwrap_or("Unknown"));
                continue;
            }
        };
        let name = db.map(|d| d.name.clone()).unwrap_or_default();
        if db.is_some() && instance_enter(c, aid, instance_id, &name, -1, -1) == IE_OK {
            // All good
            if let Some(u) = crate::unit::get(c, aid) {
                crate::clif::displaymessage(c, &u, crate::data::msg_txt(515)); // Instance has been reloaded
            }
            instance_reqinfo(c, aid, instance_id);
        } else {
            // Something went wrong
            let cid = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
            log::error!("do_reload_instance: Error setting character at instance start: character_id={cid} instance={name}.");
        }
    }
}

/// do_final_instance's counterpart: a world rebuild is a map-server restart, which starts without instances. The old
/// world's NPCs and scripts are already gone (a new module was published), so their OnInstanceDestroy cannot run
/// (deliberately different from rAthena's shutdown, which runs them): the instances, their queue and their owners'
/// ids are simply dropped, with their maps (instance_map.rs) and variables (reg.rs clear_world_regs).
pub fn on_world_rebuild(c: &Ctx) {
    for d in c.db().instance_data().iter().collect::<Vec<_>>() {
        if d.keep_timer != 0 {
            timer::delete(c, d.keep_timer);
        }
        if d.idle_timer != 0 {
            timer::delete(c, d.idle_timer);
        }
        match d.mode {
            IM_PARTY => crate::party::set_instance_id(c, d.owner_id as u32, 0),
            IM_GUILD => set_guild_instance_id(c, d.owner_id, 0),
            IM_CLAN => {
                if crate::clan::clan_search(d.owner_id).is_some() {
                    crate::clan::set_instance_id(c, d.owner_id, 0);
                }
            }
            _ => {}
        }
        c.db().instance_data().instance_id().delete(d.instance_id);
    }
    let s = state(c);
    if s.wait_timer != 0 {
        timer::delete(c, s.wait_timer);
    }
    for t in c.db().timer().iter().filter(|t| t.func == Func::InstanceSubscription as u16 || t.func == Func::InstanceDelete as u16).map(|t| t.id).collect::<Vec<_>>() {
        timer::delete(c, t);
    }
    put_state(c, &InstanceState { key: 0, instance_count: 1, wait_ids: Vec::new(), wait_timer: 0 });
    for x in c.db().pc_instance().iter().collect::<Vec<_>>() {
        c.db().pc_instance().account_id().update(PcInstance { instance_id: 0, ..x });
    }
    crate::instance_npc::on_world_rebuild(c);
    crate::instance_map::on_world_rebuild(c);
}

// ---------------------------------------------------------------------------------------------- the other files' hooks

/// pc.cpp pc_setpos's instance part (the map changes): leaving an instance's map for a map of another instance (or
/// none) updates the left instance's users (and remembers its mode for the destroy button), entering one updates the
/// entered instance's.
pub fn pc_setpos_changemap(c: &Ctx, aid: u32, cur_m: u16, new_m: u16) {
    let curr_map_instance_id = crate::instance_map::map_instance_id(c, cur_m);
    let new_map_instance_id = crate::instance_map::map_instance_id(c, new_m);
    if curr_map_instance_id != new_map_instance_id {
        if curr_map_instance_id > 0 {
            // Update instance timer for the map on leave
            instance_delusers(c, curr_map_instance_id);
            // Store mode for instance destruction button checks
            // (util::umap_find(...)->mode: a map of a destroyed instance would crash rAthena)
            if let Some(d) = find(c, curr_map_instance_id) {
                set_sd_instance_mode(c, aid, d.mode);
            }
        }
        if new_map_instance_id > 0 {
            // Update instance timer for the map on enter
            instance_addusers(c, new_map_instance_id);
        }
    }
}

/// map.cpp map_quit: `if( mapdata->instance_id > 0 ) instance_delusers(mapdata->instance_id);`
pub fn map_quit(c: &Ctx, m: u16) {
    let iid = crate::instance_map::map_instance_id(c, m);
    if iid > 0 {
        instance_delusers(c, iid);
    }
}

/// pc.cpp pc_reg_received: "Restore IM_CHAR instance to the player" (the first IM_CHAR instance this character owns).
/// (The clif_instance_info that follows is for clients before PACKETVER_RE_NUM 20190320; later clients get it at
/// clif_loadConfirm, clif_instance.rs.)
pub fn pc_reg_received(c: &Ctx, aid: u32) {
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    for id in all_ids(c) {
        let Some(d) = find(c, id) else { continue };
        if d.mode == IM_CHAR && d.owner_id == ch.id as i32 {
            set_sd_instance_id(c, aid, id);
            break;
        }
    }
}

/// unit_free: the session's instance fields go with it.
pub fn unit_free(c: &Ctx, aid: u32) {
    c.db().pc_instance().account_id().delete(aid);
}

/// pc.cpp pc_makesavestatus's instance branch (the map has no nosave flag): with instance_allow_reconnect the last
/// point is the source map at the player's cell and last_point_instanceid the instance, otherwise the save point.
/// None for a map that is not an instance map (saved normally).
pub fn makesavestatus(c: &Ctx, ch: &mut crate::character::Character, m: u16, x: i16, y: i16) -> bool {
    let Some(row) = crate::instance_map::get(c, m) else { return false };
    if row.instance_id == 0 {
        return false;
    }
    if instance_allow_reconnect() {
        // Store the original mapname
        ch.last_map = crate::data::maps().get(row.src_m).map(|d| d.name.clone()).unwrap_or_default();
        ch.last_x = x as u16;
        ch.last_y = y as u16;
        ch.last_instanceid = row.instance_id as u32;
    } else {
        // Return the user to his save point
        ch.last_map = ch.save_map.clone();
        ch.last_x = ch.save_x;
        ch.last_y = ch.save_y;
    }
    true
}

/// pc.cpp pc_lastpoint_special's instance part: None = warp to the save point, Some(name) = the instance map to log in
/// on (the stored source map name replaced by the instance map's), when the last point was in an instance
/// (last_point_instanceid) that still exists, still belongs to this character / party / guild / clan and still has
/// a copy of the map.
pub fn lastpoint_special(c: &Ctx, ch: &crate::character::Character, mapid: u16) -> Result<Option<String>, ()> {
    // Check if the last point was an instance
    if ch.last_instanceid == 0 {
        return Ok(None); // Nothing to do
    }
    // Check if returning to the instance is allowed in general
    if !instance_allow_reconnect() {
        return Err(()); // Return the player to his savepoint
    }
    let iid = ch.last_instanceid as i32;
    // Instance does not exist anymore, return the player to his savepoint
    let Some(instance_data) = find(c, iid) else { return Err(()) };
    match instance_data.mode {
        IM_NONE => {} // Cannot validate
        IM_CHAR => {
            if ch.id as i32 != instance_data.owner_id {
                return Err(());
            }
        }
        IM_PARTY => {
            if ch.party_id as i32 != instance_data.owner_id {
                return Err(());
            }
        }
        IM_GUILD => {
            if ch.guild_id as i32 != instance_data.owner_id {
                return Err(());
            }
        }
        IM_CLAN => {
            if ch.clan_id as i32 != instance_data.owner_id {
                return Err(());
            }
        }
        _ => {}
    }
    let imapid = instance_mapid(c, mapid as i32, iid);
    if imapid < 0 {
        // Instance does not contain this map anymore, return the player to his savepoint
        return Err(());
    }
    // Overwrite the stored "source mapname" with the real mapname
    Ok(Some(crate::instance_map::instance_generate_mapname(mapid as i32, iid)))
}

/// script.cpp script_instancegetid: IM_NONE = the running NPC's instance, the other modes the attached player's
/// (its own IM_CHAR instance, its party's, guild's or clan's). 0 when there is none.
pub fn script_instancegetid(c: &Ctx, oid: u32, rid: u32, mode: u8) -> i32 {
    if mode == IM_NONE {
        return crate::instance_npc::npc_instance_id(c, oid);
    }
    if crate::pc::get(c, rid).is_none() {
        return 0;
    }
    let (_, party_id, guild_id, clan_id) = status_ids(c, rid);
    let id = match mode {
        IM_CHAR => sd_instance_id(c, rid),
        IM_PARTY => party_instance_id(c, party_id).unwrap_or(0),
        IM_GUILD => guild_instance_id(c, guild_id).unwrap_or(0),
        IM_CLAN => clan_instance_id(c, clan_id).unwrap_or(0),
        _ => 0, // Unsupported type
    };
    if id > 0 {
        id
    } else {
        0
    }
}

/// The `'` variables of the running script (get_val / set_reg / script_array_src): the instance of the NPC
/// (script_instancegetid(st)), when that instance exists (util::umap_find(instances, ...)).
pub fn script_regs_instance(c: &Ctx, oid: u32) -> Option<i32> {
    let iid = script_instancegetid(c, oid, 0, IM_NONE);
    find(c, iid).map(|_| iid)
}

use crate::timer::timer as _;
use crate::pc::pc as _;
