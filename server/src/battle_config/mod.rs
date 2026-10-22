//! battle_config at run time (port wave I, iconf): rAthena's `struct Battle_Config battle_config` (map/battle.hpp), the
//! `battle_data[]` table that names its members (battle.cpp:8246-8913) and the functions that fill it
//! (battle.cpp:8918 battle_set_value, :8939 battle_get_value, :8952 battle_set_defaults, :8962 battle_adjust_conf,
//! :9158 battle_config_read).
//!
//! **One accessor.** Every rule reads a setting with [`get`]`(Bc::NAME)`, which is rAthena's `battle_config.member`:
//! one load from an array in the module's memory, no table row. conf.rs and the systems' conf files wrap it per
//! setting (`conf::area_size()`), so that a reader keeps the type it had as a constant.
//!
//! **Where the values live.**
//! - The built-in values, [`DEFAULTS`]: what the reference map-server runs with (script/battle_flags.rs: conf/battle/
//!   *.conf + the overlay's conf/import/battle_conf.txt, after battle_adjust_conf). A database nobody changed has no
//!   row at all and every setting is the constant it was before this wave.
//! - Table `battle_config` (private): one row per setting whose value is NOT the built-in one, whoever changed it
//!   (battle_set_value: @setbattleflag, the `setbattleflag` script command, the owner's `set_battle_flag`; or a conf
//!   read with the host's import file). Table `battle_config_state` (one row): a stamp of those rows.
//! - Table `battle_conf_import` (private, one row): the host's conf/import/battle_conf.txt as text, with what the
//!   map-server would have printed when it read it (`report`). No row: the overlay's own import file, built in.
//! - The module's memory: the whole table as an array, refreshed by [`load`] at the start of every reducer call
//!   (world.rs Ctx::new, as the tick and the release switch are): ONE primary-key lookup of the stamp row; the rows
//!   are read again only when the stamp is not the one in memory (another call changed a setting, or this instance's
//!   last call failed and was rolled back).
//!
//! **Life of a value.** battle_set_value stores the value as it is given, with battle_data's range check and no
//! battle_adjust_conf (battle.cpp:8918-8934). [`battle_config_read`] is rAthena's `battle_config_read(
//! "conf/battle_athena.conf")`: battle_set_defaults, conf/battle/*.conf, the import file, battle_adjust_conf; it runs
//! when the world is built (npc.rs build_world: the map-server's start), at @reloadbattleconf, and when the owner
//! gives or removes an import file. So what battle_set_value changed lasts until the next of those, as on rAthena,
//! and the host's file is read again every time.
//!
//! **Settings a database consumes when it loads.** rAthena multiplies a monster's EXP by base_exp_rate / job_exp_rate
//! / mvp_exp_rate while it reads mob_db (mob.cpp:5077-5095) and adjusts every drop with the item_rate_* /
//! item_drop_*_min / _max members right after (mob.cpp:6773 mob_drop_ratio_adjust), so a new rate only shows after
//! mob_reload (the <reload> argument of @setbattleflag / setbattleflag, @reloadmobdb, @reloadbattleconf when a rate
//! changed). The module keeps that moment too: [`mob_load`] copies those members ([`MOBDB_MEMBERS`]) into the state
//! row, and mob/rtdb.rs makes the monsters with the copy ([`mobdb`]), not with the live members.
//!
//! Not every setting is read by the module's rules yet: [`converted::CONVERTED`] lists the ones whose named readers
//! all are run-time reads. Setting another one is stored and shown by getbattleflag as rAthena would, and logged as
//! not followed (or not wholly) by the rules.

pub mod converted;
pub mod data;

use std::cell::Cell;

use spacetimedb::{reducer, table, ReducerContext, Table};

pub use data::{BATTLE_DATA, N};

use crate::world::Ctx;

/// A member of `struct Battle_Config`: its position in battle_data[] (data.rs names every one: `Bc::AREA_SIZE`).
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct Bc(pub(crate) u16);

/// One line of battle_data[] (battle.cpp:8246-8252): the setting's name, default, minimum and maximum; `conf` is the
/// value rAthena's own conf/battle/*.conf give it (before the import file and battle_adjust_conf).
pub struct BattleData {
    pub name: &'static str,
    pub defval: i32,
    pub min: i32,
    pub max: i32,
    pub conf: i32,
}

/// status.hpp:61-67, mmo.hpp:93.
const AMOTION_DIVIDER_PC: i32 = 2;
const AMOTION_ZERO_ASPD: i32 = 2000;
const AMOTION_INTERVAL: i32 = 10;
const DEFAULT_WALK_SPEED: i32 = 150;

/// The overlay's conf/import/battle_conf.txt, the import file of a database whose owner gave none.
pub const BUILTIN_IMPORT: &[u8] = include_bytes!("../../data/overlay/conf/import/battle_conf.txt");

/// battle_config as the reference map-server runs it (script/battle_flags.rs, in battle_data order): the value of
/// every setting nobody changed. `battle_config_read` of the built-in files gives exactly this (unit test
/// `builtin_conf_is_the_reference`).
pub const DEFAULTS: [i32; N] = {
    let f = crate::script::battle_flags::BATTLE_FLAGS;
    assert!(f.len() == N);
    let mut a = [0i32; N];
    let mut i = 0;
    while i < N {
        a[i] = f[i].1;
        i += 1;
    }
    a
};

thread_local! {
    /// battle_config for this reducer call ([`load`]).
    static VALUES: [Cell<i32>; N] = const {
        let mut a = [const { Cell::new(0) }; N];
        let mut i = 0;
        while i < N {
            a[i] = Cell::new(DEFAULTS[i]);
            i += 1;
        }
        a
    };
    /// The stamp of the rows VALUES was made from (0: no row, the built-in values).
    static STAMP: Cell<u64> = const { Cell::new(0) };
    /// [`MOBDB_MEMBERS`] as mob_db was last loaded with.
    static MOBDB: [Cell<i32>; MOBDB_N] = const {
        let mut a = [const { Cell::new(0) }; MOBDB_N];
        let mut i = 0;
        while i < MOBDB_N {
            a[i] = Cell::new(DEFAULTS[MOBDB_MEMBERS[i].0 as usize]);
            i += 1;
        }
        a
    };
}

/// The members mob_db consumes while it loads and that the module applies itself at a mob_reload (mob/rtdb.rs):
/// MobDatabase::parseBodyNode's EXP rates (mob.cpp:5077 base_exp, :5086 job_exp, :5095 mexp) and everything
/// mob_drop_ratio_adjust and mob_drop_adjust read (mob.cpp:6773-6913, :4808-4821). tools/datagen_mods/mobs.py
/// MOBDB_CONF_NAMES is the same list: the embedded mob_db says which values it was made with.
///
/// Then the members of MobDatabase::loadingFinished and the DamageMotion node (mob.cpp:5409 monster_damage_delay_rate,
/// :5556 view_range_rate, :5559 chase_range_rate, :5567 monster_active_enable, :5582 mvp_hp_rate, :5585
/// monster_hp_rate): the module applies them again as long as the embedded data was made with their neutral value
/// (100, or 1 for monster_active_enable), which is when the embedded value is the file's own.
///
/// The other members mob_db consumes at load (mob_max_skilllvl, mob_skill_rate, mob_skill_delay for the skill lists;
/// what status_calc_misc reads: enable_critical, mob_critical_rate, critical_rate, enable_baseatk_renewal,
/// enable_perfect_flee) are baked into the data by datagen and need a build.
pub const MOBDB_MEMBERS: [Bc; MOBDB_N] = [
    Bc::BASE_EXP_RATE,
    Bc::JOB_EXP_RATE,
    Bc::MVP_EXP_RATE,
    Bc::ITEM_LOGARITHMIC_DROPS,
    Bc::DROP_RATEINCREASE,
    Bc::ITEM_RATE_MVP,
    Bc::ITEM_DROP_MVP_MIN,
    Bc::ITEM_DROP_MVP_MAX,
    Bc::ITEM_RATE_TREASURE,
    Bc::ITEM_DROP_TREASURE_MIN,
    Bc::ITEM_DROP_TREASURE_MAX,
    Bc::ITEM_RATE_HEAL,
    Bc::ITEM_RATE_HEAL_BOSS,
    Bc::ITEM_RATE_HEAL_MVP,
    Bc::ITEM_DROP_HEAL_MIN,
    Bc::ITEM_DROP_HEAL_MAX,
    Bc::ITEM_RATE_USE,
    Bc::ITEM_RATE_USE_BOSS,
    Bc::ITEM_RATE_USE_MVP,
    Bc::ITEM_DROP_USE_MIN,
    Bc::ITEM_DROP_USE_MAX,
    Bc::ITEM_RATE_EQUIP,
    Bc::ITEM_RATE_EQUIP_BOSS,
    Bc::ITEM_RATE_EQUIP_MVP,
    Bc::ITEM_DROP_EQUIP_MIN,
    Bc::ITEM_DROP_EQUIP_MAX,
    Bc::ITEM_RATE_CARD,
    Bc::ITEM_RATE_CARD_BOSS,
    Bc::ITEM_RATE_CARD_MVP,
    Bc::ITEM_DROP_CARD_MIN,
    Bc::ITEM_DROP_CARD_MAX,
    Bc::ITEM_RATE_COMMON,
    Bc::ITEM_RATE_COMMON_BOSS,
    Bc::ITEM_RATE_COMMON_MVP,
    Bc::ITEM_DROP_COMMON_MIN,
    Bc::ITEM_DROP_COMMON_MAX,
    Bc::MONSTER_DAMAGE_DELAY_RATE,
    Bc::VIEW_RANGE_RATE,
    Bc::CHASE_RANGE_RATE,
    Bc::MONSTER_ACTIVE_ENABLE,
    Bc::MVP_HP_RATE,
    Bc::MONSTER_HP_RATE,
];
pub const MOBDB_N: usize = 42;

/// [`MOBDB_MEMBERS`]' values at one moment, in that order.
pub type MobDbConf = [i32; MOBDB_N];

/// [`MOBDB_MEMBERS`] as mob_db was last loaded with ([`mob_load`]).
pub fn mobdb() -> MobDbConf {
    let mut a = [0i32; MOBDB_N];
    MOBDB.with(|m| {
        for j in 0..MOBDB_N {
            a[j] = m[j].get();
        }
    });
    a
}

/// One member of a [`MobDbConf`].
pub fn mobdb_at(conf: &MobDbConf, k: Bc) -> i32 {
    match MOBDB_MEMBERS.iter().position(|m| *m == k) {
        Some(j) => conf[j],
        None => panic!("{} is not a member mob_db consumes", BATTLE_DATA[k.0 as usize].name),
    }
}

/// [`MOBDB_MEMBERS`] in a whole battle_config.
fn mobdb_of(a: &[i32; N]) -> MobDbConf {
    let mut m = [0i32; MOBDB_N];
    for j in 0..MOBDB_N {
        m[j] = a[MOBDB_MEMBERS[j].0 as usize];
    }
    m
}

fn mobdb_defaults() -> MobDbConf {
    mobdb_of(&DEFAULTS)
}

/// `battle_config.<member>`: THE accessor. A read of the module's memory, valid for the reducer call [`load`] ran in.
#[inline]
pub fn get(k: Bc) -> i32 {
    VALUES.with(|v| v[k.0 as usize].get())
}

/// One setting whose value is not the built-in one (`name` is battle_data's spelling).
#[table(accessor = battle_config)]
#[derive(Clone, Debug)]
pub struct BattleConfigRow {
    #[primary_key]
    pub name: String,
    pub value: i32,
}

/// The stamp of the `battle_config` rows and of `mobdb` (key 0; no row: no rows, and mob_db loaded with the built-in
/// values). Written with them, by this file only. `mobdb`: [`MOBDB_MEMBERS`] as mob_db was last loaded with (empty:
/// the built-in values).
#[table(accessor = battle_config_state)]
pub struct BattleConfigState {
    #[primary_key]
    pub key: u8,
    pub stamp: u64,
    pub mobdb: Vec<i32>,
}

/// The host's conf/import/battle_conf.txt (key 0), set by the owner (`set_battle_conf`). `report`: the warnings of
/// the last read (rAthena prints them on the map-server's console), one per line.
#[table(accessor = battle_conf_import)]
pub struct BattleConfImport {
    #[primary_key]
    pub key: u8,
    pub text: String,
    pub report: String,
}

/// A stamp of a whole battle_config and of the mob_db copy: 0 when both are the built-in ones, else a 64-bit FNV-1a
/// hash of the (position, value) pairs that differ. Two states with one stamp are the same state, whatever calls led
/// to them: that is what makes [`load`] safe after a rolled-back call.
fn stamp_of(v: &[i32; N], mobdb: &MobDbConf) -> u64 {
    let mut h: u64 = 0xcbf2_9ce4_8422_2325;
    let mut any = false;
    let mut mix = |pos: u16, val: i32| {
        for b in pos.to_le_bytes().into_iter().chain(val.to_le_bytes()) {
            h ^= b as u64;
            h = h.wrapping_mul(0x0000_0100_0000_01b3);
        }
    };
    for i in 0..N {
        if v[i] != DEFAULTS[i] {
            any = true;
            mix(i as u16, v[i]);
        }
    }
    let md = mobdb_defaults();
    for j in 0..MOBDB_N {
        if mobdb[j] != md[j] {
            any = true;
            mix(0x8000 | j as u16, mobdb[j]);
        }
    }
    match (any, h) {
        (false, _) => 0,
        (true, 0) => 1,
        (true, h) => h,
    }
}

fn snapshot() -> [i32; N] {
    let mut a = [0i32; N];
    VALUES.with(|v| {
        for i in 0..N {
            a[i] = v[i].get();
        }
    });
    a
}

fn set_memory(a: &[i32; N], mobdb: &MobDbConf, stamp: u64) {
    VALUES.with(|v| {
        for i in 0..N {
            v[i].set(a[i]);
        }
    });
    MOBDB.with(|m| {
        for j in 0..MOBDB_N {
            m[j].set(mobdb[j]);
        }
    });
    STAMP.with(|s| s.set(stamp));
}

/// Writes the state row for a battle_config and a mob_db copy, and returns its stamp.
fn write_state(db: &spacetimedb::Local, a: &[i32; N], mobdb: &MobDbConf) -> u64 {
    let stamp = stamp_of(a, mobdb);
    db.battle_config_state().key().delete(0);
    if stamp != 0 {
        let mobdb = if *mobdb == mobdb_defaults() { Vec::new() } else { mobdb.to_vec() };
        db.battle_config_state().insert(BattleConfigState { key: 0, stamp, mobdb });
    }
    stamp
}

/// mob_db is being loaded (mob.cpp mob_load: do_init_mob at the map-server's start, mob_reload later): the members it
/// consumes are copied as they are now.
pub fn mob_load(c: &Ctx) {
    let a = snapshot();
    let now = mobdb_of(&a);
    if now == mobdb() {
        return;
    }
    let stamp = write_state(c.db(), &a, &now);
    set_memory(&a, &now, stamp);
}

/// battle_data's line for a name: ARR_FIND(..., strcmpi(w1, battle_data[i].str) == 0), the first match.
pub fn find(name: &str) -> Option<usize> {
    let b = crate::net::wire::latin1(name);
    BATTLE_DATA.iter().position(|d| d.name.as_bytes().eq_ignore_ascii_case(&b))
}

/// Brings the module's memory to this call's battle_config (world.rs Ctx::new): one lookup of the stamp row; the
/// rows only when it differs from the stamp in memory.
pub fn load(db: &spacetimedb::Local) {
    let state = db.battle_config_state().key().find(0);
    let stamp = state.as_ref().map(|r| r.stamp).unwrap_or(0);
    if STAMP.with(|s| s.get()) == stamp {
        return;
    }
    let mut a = DEFAULTS;
    let mut mobdb = mobdb_defaults();
    if let Some(state) = state {
        for row in db.battle_config().iter() {
            if let Some(i) = find(&row.name) {
                a[i] = row.value;
            }
        }
        if state.mobdb.len() == MOBDB_N {
            mobdb.copy_from_slice(&state.mobdb);
        }
    }
    set_memory(&a, &mobdb, stamp);
}

/// Writes a whole battle_config: the rows that differ from the built-in values, their stamp, the memory.
fn store(db: &spacetimedb::Local, a: &[i32; N]) {
    let old: Vec<BattleConfigRow> = db.battle_config().iter().collect();
    for r in &old {
        match find(&r.name) {
            Some(i) if a[i] != DEFAULTS[i] && a[i] == r.value && BATTLE_DATA[i].name == r.name => {}
            _ => {
                db.battle_config().name().delete(r.name.clone());
            }
        }
    }
    for i in 0..N {
        // (a name battle_data lists twice is only ever found at its first line)
        if a[i] != DEFAULTS[i] && find(BATTLE_DATA[i].name) == Some(i) && !old.iter().any(|r| r.name == BATTLE_DATA[i].name && r.value == a[i]) {
            db.battle_config().insert(BattleConfigRow { name: BATTLE_DATA[i].name.to_string(), value: a[i] });
        }
    }
    let mobdb = mobdb();
    let stamp = write_state(db, a, &mobdb);
    set_memory(a, &mobdb, stamp);
}

/// `*battle_data[i].val = val` for one member.
fn set_member(db: &spacetimedb::Local, i: usize, val: i32) {
    let mut a = snapshot();
    if a[i] == val {
        return;
    }
    a[i] = val;
    let name = BATTLE_DATA[i].name.to_string();
    db.battle_config().name().delete(name.clone());
    if val != DEFAULTS[i] {
        db.battle_config().insert(BattleConfigRow { name, value: val });
    }
    let stamp = write_state(db, &a, &mobdb());
    VALUES.with(|v| v[i].set(val));
    STAMP.with(|s| s.set(stamp));
}

/// common/strlib.cpp:239 config_switch: on / yes / oui / ja / si / sim are 1, off / no / non / nein / nao are 0
/// (strcmpi), anything else `(int32)strtol(str, nullptr, 0)`.
pub fn config_switch(s: &[u8]) -> i32 {
    const ON: [&[u8]; 6] = [b"on", b"yes", b"oui", b"ja", b"si", b"sim"];
    const OFF: [&[u8]; 5] = [b"off", b"no", b"non", b"nein", b"nao"];
    if ON.iter().any(|w| w.eq_ignore_ascii_case(s)) {
        return 1;
    }
    if OFF.iter().any(|w| w.eq_ignore_ascii_case(s)) {
        return 0;
    }
    strtol0(s) as i32
}

/// glibc strtol(str, nullptr, 0) on a 64-bit long: white space, a sign, "0x" / "0X" for hexadecimal (only when a hex
/// digit follows), a leading 0 for octal, else decimal; LONG_MAX / LONG_MIN on overflow; 0 when no digit is read.
fn strtol0(s: &[u8]) -> i64 {
    let mut i = 0;
    while i < s.len() && matches!(s[i], b' ' | b'\t' | b'\n' | 0x0b | 0x0c | b'\r') {
        i += 1;
    }
    let neg = match s.get(i) {
        Some(b'-') => {
            i += 1;
            true
        }
        Some(b'+') => {
            i += 1;
            false
        }
        _ => false,
    };
    let digit = |b: u8| -> u32 {
        match b {
            b'0'..=b'9' => (b - b'0') as u32,
            b'a'..=b'z' => (b - b'a') as u32 + 10,
            b'A'..=b'Z' => (b - b'A') as u32 + 10,
            _ => 99,
        }
    };
    let base: u32 = if s.get(i) == Some(&b'0') && matches!(s.get(i + 1), Some(b'x') | Some(b'X')) && s.get(i + 2).is_some_and(|&b| digit(b) < 16) {
        i += 2;
        16
    } else if s.get(i) == Some(&b'0') {
        8
    } else {
        10
    };
    let mut v: i128 = 0;
    while i < s.len() && digit(s[i]) < base {
        v = (v * base as i128 + digit(s[i]) as i128).min(1 << 70);
        i += 1;
    }
    let v = if neg { -v } else { v };
    v.clamp(i64::MIN as i128, i64::MAX as i128) as i64
}

/// battle.cpp:8918 battle_set_value(w1, w2) on a table that is being read (battle_config_read): 0 when battle_data has
/// no such name; a value outside [min, max] becomes the default, with rAthena's warning.
fn set_value_in(a: &mut [i32; N], w1: &[u8], w2: &[u8], warnings: &mut Vec<String>) -> i32 {
    let val = config_switch(w2);
    let Some(i) = BATTLE_DATA.iter().position(|d| d.name.as_bytes().eq_ignore_ascii_case(w1)) else { return 0 };
    let d = &BATTLE_DATA[i];
    let val = if val < d.min || val > d.max {
        warnings.push(format!(
            "Value for setting '{}': {} is invalid (min:{} max:{})! Defaulting to {}...",
            String::from_utf8_lossy(w1),
            String::from_utf8_lossy(w2),
            d.min,
            d.max,
            d.defval
        ));
        d.defval
    } else {
        val
    };
    a[i] = val;
    1
}

/// battle.cpp:8918 battle_set_value(w1, w2): `val = config_switch(w2)`; 0 when battle_data has no such name (strcmpi);
/// a value outside [min, max] becomes the DEFAULT with a warning (battle.cpp:8927-8930), it is not refused; the
/// member is set as it is given, with no battle_adjust_conf; 1.
pub fn battle_set_value(c: &Ctx, w1: &str, w2: &str) -> i32 {
    set_value_db(c.db(), w1, w2)
}

fn set_value_db(db: &spacetimedb::Local, w1: &str, w2: &str) -> i32 {
    let mut a = snapshot();
    let mut warnings = Vec::new();
    let (b1, b2) = (crate::net::wire::latin1(w1), crate::net::wire::latin1(w2));
    if set_value_in(&mut a, &b1, &b2, &mut warnings) == 0 {
        return 0; // not found
    }
    for w in &warnings {
        log::warn!("{w}");
    }
    let Some(i) = find(w1) else { return 0 };
    set_member(db, i, a[i]);
    if !converted::is_converted(Bc(i as u16)) && a[i] != DEFAULTS[i] {
        log::warn!("{}", not_read_yet(i, a[i]));
    }
    1
}

/// What the log (and the import's report) says about a setting the module's rules do not follow, or not wholly: it
/// has no run-time reader (a branch ported for the reference value only, or never needed), a build constant is left
/// for it, or datagen baked it into the embedded data (converted.rs; the lists are in server/ARCHITECTURE.md).
fn not_read_yet(i: usize, val: i32) -> String {
    format!(
        "battle_config '{}' is now {} (getbattleflag shows it), but the module's rules do not follow this setting, or not wholly: built with {} (server/ARCHITECTURE.md)",
        BATTLE_DATA[i].name, val, DEFAULTS[i]
    )
}

/// battle.cpp:8939 battle_get_value(w1): the member's value now, 0 when battle_data has no such name.
pub fn battle_get_value(w1: &str) -> i32 {
    find(w1).map(|i| get(Bc(i as u16))).unwrap_or(0)
}

/// battle.cpp:8962 battle_adjust_conf, as this build compiles it (PACKETVER 20211103, RENEWAL, no CELL_NOSTACK, no
/// MAP_GENERATOR). The PACKETVER blocks that are compiled out are not listed; int32 arithmetic wraps as the C does.
pub fn battle_adjust_conf(a: &mut [i32; N], warnings: &mut Vec<String>) {
    let at = |k: Bc| k.0 as usize;
    // Effectively we calculate the minimum delay between attacks here
    for k in [Bc::MAX_ASPD, Bc::MAX_THIRD_ASPD, Bc::MAX_SUMMONER_ASPD, Bc::MAX_EXTENDED_ASPD] {
        a[at(k)] = AMOTION_ZERO_ASPD.wrapping_sub(a[at(k)].wrapping_mul(AMOTION_INTERVAL)).wrapping_mul(AMOTION_DIVIDER_PC);
    }
    // (max_walk_speed is at least 100: battle.cpp:8403)
    a[at(Bc::MAX_WALK_SPEED)] = (100 * DEFAULT_WALK_SPEED).checked_div(a[at(Bc::MAX_WALK_SPEED)]).unwrap_or(0);
    a[at(Bc::MAX_CART_WEIGHT)] = a[at(Bc::MAX_CART_WEIGHT)].wrapping_mul(10);

    if a[at(Bc::MAX_DEF)] > 100 && a[at(Bc::WEAPON_DEFENSE_TYPE)] == 0 {
        a[at(Bc::MAX_DEF)] = 100;
    }
    if a[at(Bc::MIN_HITRATE)] > a[at(Bc::MAX_HITRATE)] {
        a[at(Bc::MIN_HITRATE)] = a[at(Bc::MAX_HITRATE)];
    }
    if a[at(Bc::PET_MAX_ATK1)] > a[at(Bc::PET_MAX_ATK2)] {
        a[at(Bc::PET_MAX_ATK1)] = a[at(Bc::PET_MAX_ATK2)];
    }
    if a[at(Bc::DAY_DURATION)] != 0 && a[at(Bc::DAY_DURATION)] < 60000 {
        a[at(Bc::DAY_DURATION)] = 60000;
    }
    if a[at(Bc::NIGHT_DURATION)] != 0 && a[at(Bc::NIGHT_DURATION)] < 60000 {
        a[at(Bc::NIGHT_DURATION)] = 60000;
    }
    // #elif PACKETVER >= 20141112 (battle.cpp:9026-9031)
    if a[at(Bc::FEATURE_AUCTION)] != 0 {
        warnings.push("conf/battle/feature.conf:feature.auction is enabled but it is not available for clients from 2014-11-12 on, disabling...".into());
        warnings.push("conf/battle/feature.conf:feature.auction change value to '2' to silence this warning and maintain it enabled".into());
        a[at(Bc::FEATURE_AUCTION)] = 0;
    }
    // #if PACKETVER < 20230302 (battle.cpp:9136-9143)
    if a[at(Bc::FEATURE_MESITEMICON)] != 0 {
        warnings.push("conf/battle/feature.conf:mesitemicon is enabled but it requires PACKETVER 2023-03-02 or newer, disabling...".into());
        a[at(Bc::FEATURE_MESITEMICON)] = 0;
    }
    // #ifndef CELL_NOSTACK (battle.cpp:9145-9148)
    if a[at(Bc::CUSTOM_CELL_STACK_LIMIT)] != 1 {
        warnings.push("Battle setting 'custom_cell_stack_limit' takes no effect as this server was compiled without Cell Stack Limit support.".into());
    }
}

/// fgets(line, 1024, fp): the next line with its '\n', at most 1023 bytes.
fn fgets<'a>(rest: &mut &'a [u8]) -> Option<&'a [u8]> {
    if rest.is_empty() {
        return None;
    }
    let cap = rest.len().min(1023);
    let n = rest[..cap].iter().position(|&b| b == b'\n').map(|p| p + 1).unwrap_or(cap);
    let (line, r) = rest.split_at(n);
    *rest = r;
    Some(line)
}

/// sscanf(line, "%1023[^:]:%1023s", w1, w2) == 2: w1 is everything before the first ':' (at least one byte, white
/// space included), w2 the first run of non-white-space bytes after it.
fn scan_line(line: &[u8]) -> Option<(&[u8], &[u8])> {
    // (a C string ends at its first NUL)
    let line = &line[..line.iter().position(|&b| b == 0).unwrap_or(line.len())];
    let n1 = line.iter().take(1023).position(|&b| b == b':').unwrap_or(line.len().min(1023));
    if n1 == 0 || line.get(n1) != Some(&b':') {
        return None;
    }
    let rest = &line[n1 + 1..];
    let is_space = |b: u8| matches!(b, b' ' | b'\t' | b'\n' | 0x0b | 0x0c | b'\r');
    let start = rest.iter().position(|&b| !is_space(b))?;
    let rest = &rest[start..];
    let n2 = rest.iter().take(1023).position(|&b| is_space(b)).unwrap_or(rest.len().min(1023));
    Some((&line[..n1], &rest[..n2]))
}

/// battle.cpp:9158 battle_config_read("conf/battle_athena.conf") with `import` as conf/import/battle_conf.txt:
/// battle_set_defaults and rAthena's own conf/battle/*.conf (data.rs `conf`, read when the table was generated), the
/// import file line by line (battle.cpp:9174-9201), battle_adjust_conf. Returns the table and what the map-server
/// would have printed.
pub fn read_conf(import: &[u8]) -> ([i32; N], Vec<String>) {
    let mut a = [0i32; N];
    for i in 0..N {
        a[i] = BATTLE_DATA[i].conf;
    }
    let mut warnings = Vec::new();
    let mut rest = import;
    while let Some(line) = fgets(&mut rest) {
        if line.starts_with(b"//") {
            continue;
        }
        let Some((w1, w2)) = scan_line(line) else { continue };
        if w1.eq_ignore_ascii_case(b"import") {
            // battle_config_read(w2): the module has no files to open (battle.cpp:9168-9170)
            warnings.push(format!("File not found: {}", String::from_utf8_lossy(w2)));
        } else if w1.eq_ignore_ascii_case(b"atcommand_symbol") || w1.eq_ignore_ascii_case(b"charcommand_symbol") {
            // battle.cpp:9181-9198 change the command symbols: the module's are constants ('@' and '#')
            warnings.push(format!("{}: the module's command symbols are '@' and '#' and cannot be changed", String::from_utf8_lossy(w1)));
        } else if set_value_in(&mut a, w1, w2, &mut warnings) == 0 {
            warnings.push(format!("Unknown setting '{}' in file conf/import/battle_conf.txt", String::from_utf8_lossy(w1)));
        }
    }
    battle_adjust_conf(&mut a, &mut warnings);
    (a, warnings)
}

/// The members atcommand.cpp:4443-4478 compares after a conf read: a change of any of them needs mob_reload.
const RATE_MEMBERS: [Bc; 35] = [
    Bc::ITEM_RATE_MVP,
    Bc::ITEM_RATE_COMMON,
    Bc::ITEM_RATE_COMMON_BOSS,
    Bc::ITEM_RATE_COMMON_MVP,
    Bc::ITEM_RATE_CARD,
    Bc::ITEM_RATE_CARD_BOSS,
    Bc::ITEM_RATE_CARD_MVP,
    Bc::ITEM_RATE_EQUIP,
    Bc::ITEM_RATE_EQUIP_BOSS,
    Bc::ITEM_RATE_EQUIP_MVP,
    Bc::ITEM_RATE_HEAL,
    Bc::ITEM_RATE_HEAL_BOSS,
    Bc::ITEM_RATE_HEAL_MVP,
    Bc::ITEM_RATE_USE,
    Bc::ITEM_RATE_USE_BOSS,
    Bc::ITEM_RATE_USE_MVP,
    Bc::ITEM_RATE_TREASURE,
    Bc::ITEM_RATE_ADDDROP,
    Bc::ITEM_LOGARITHMIC_DROPS,
    Bc::ITEM_DROP_COMMON_MIN,
    Bc::ITEM_DROP_COMMON_MAX,
    Bc::ITEM_DROP_CARD_MIN,
    Bc::ITEM_DROP_CARD_MAX,
    Bc::ITEM_DROP_EQUIP_MIN,
    Bc::ITEM_DROP_EQUIP_MAX,
    Bc::ITEM_DROP_MVP_MIN,
    Bc::ITEM_DROP_MVP_MAX,
    Bc::ITEM_DROP_HEAL_MIN,
    Bc::ITEM_DROP_HEAL_MAX,
    Bc::ITEM_DROP_USE_MIN,
    Bc::ITEM_DROP_USE_MAX,
    Bc::ITEM_DROP_TREASURE_MIN,
    Bc::ITEM_DROP_TREASURE_MAX,
    Bc::BASE_EXP_RATE,
    Bc::JOB_EXP_RATE,
];

/// The import file this database reads: the owner's, else the built-in one.
fn import_text(db: &spacetimedb::Local) -> Vec<u8> {
    match db.battle_conf_import().key().find(0) {
        Some(r) => r.text.into_bytes(),
        None => BUILTIN_IMPORT.to_vec(),
    }
}

/// battle_config_read(BATTLE_CONF_FILENAME) (battle.cpp:9158; called by do_init at the map-server's start and by
/// @reloadbattleconf): every member gets its conf value again, so what battle_set_value changed is gone. True when a
/// drop or EXP rate is not what it was (atcommand.cpp:4443-4478: the caller then runs mob_reload).
pub fn battle_config_read(c: &Ctx) -> bool {
    read_db(c.db())
}

fn read_db(db: &spacetimedb::Local) -> bool {
    let prev = snapshot();
    let (a, warnings) = read_conf(&import_text(db));
    let mut report = warnings;
    for i in 0..N {
        if a[i] != DEFAULTS[i] && !converted::is_converted(Bc(i as u16)) {
            report.push(not_read_yet(i, a[i]));
        }
    }
    for w in &report {
        log::warn!("battle_config_read: {w}");
    }
    if let Some(mut row) = db.battle_conf_import().key().find(0) {
        let text = report.join("\n");
        if row.report != text {
            row.report = text;
            db.battle_conf_import().key().update(row);
        }
    }
    store(db, &a);
    RATE_MEMBERS.iter().any(|k| prev[k.0 as usize] != a[k.0 as usize])
}

/// The owner gives the server's conf/import/battle_conf.txt as text (rAthena's format: `name: value` lines, `//`
/// comments): it replaces the built-in import file, is read at once as @reloadbattleconf reads the files (so what
/// @setbattleflag changed is gone, and monsters are reloaded when a drop or EXP rate changed), and again at every
/// world build. An empty text is an empty file: rAthena's own conf/battle values. What the map-server would print
/// while reading (unknown names, values out of range, settings the module does not read yet) is kept in
/// `battle_conf_import.report` (`SELECT report FROM battle_conf_import`) and in the module's log.
#[reducer]
pub fn set_battle_conf(ctx: &ReducerContext, text: String) -> Result<(), String> {
    crate::release::require_owner(ctx, "change the battle configuration")?;
    if text.len() > 256 * 1024 {
        return Err("The battle configuration text is larger than 256 KiB.".into());
    }
    ctx.db.battle_conf_import().key().delete(0);
    ctx.db.battle_conf_import().insert(BattleConfImport { key: 0, text, report: String::new() });
    reread(ctx);
    Ok(())
}

/// The owner removes the import file given with `set_battle_conf`: the built-in one is read again.
#[reducer]
pub fn clear_battle_conf(ctx: &ReducerContext) -> Result<(), String> {
    crate::release::require_owner(ctx, "change the battle configuration")?;
    ctx.db.battle_conf_import().key().delete(0);
    reread(ctx);
    Ok(())
}

/// After the import file changed: battle_config_read as the map-server's start makes it, where mob_db loads after the
/// conf: mob_reload when a rate changed (ACMD_FUNC(reloadbattleconf), atcommand.cpp:4436-4485) or when any member
/// mob_db consumes is no longer what mob_db was loaded with (mvp_exp_rate and drop_rateincrease are not among the
/// rates the command compares).
fn reread(ctx: &ReducerContext) {
    crate::world::run(ctx, |c| {
        let rates_changed = battle_config_read(c);
        if rates_changed || mobdb_of(&snapshot()) != mobdb() {
            crate::atcommand_atcmd::admin::mob_reload(c);
        }
    });
}

/// The owner's @setbattleflag (ACMD_FUNC(setbattleflag), atcommand.cpp:7781): battle_set_value(name, value), then
/// mob_reload when `reload`. Like the command's, the change lasts until the next conf read (@reloadbattleconf, a
/// world build, `set_battle_conf`); a lasting change belongs in the import file.
#[reducer]
pub fn set_battle_flag(ctx: &ReducerContext, name: String, value: String, reload: bool) -> Result<(), String> {
    crate::release::require_owner(ctx, "change the battle configuration")?;
    let mut known = true;
    crate::world::run(ctx, |c| {
        if battle_set_value(c, &name, &value) == 0 {
            known = false;
        } else if reload {
            crate::atcommand_atcmd::admin::mob_reload(c);
        }
    });
    if known { Ok(()) } else { Err("Unknown battle_config flag.".into()) }
}

/// Unit tests of other files set a member for the test's thread.
#[cfg(test)]
pub fn set_for_test(k: Bc, val: i32) {
    VALUES.with(|v| v[k.0 as usize].set(val));
}

/// Unit tests: the built-in table again.
#[cfg(test)]
pub fn reset_for_test() {
    set_memory(&DEFAULTS, &mobdb_defaults(), 0);
}

/// Unit tests: mob_db loaded with these values.
#[cfg(test)]
pub fn set_mobdb_for_test(conf: MobDbConf) {
    MOBDB.with(|m| {
        for j in 0..MOBDB_N {
            m[j].set(conf[j]);
        }
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    /// data.rs is battle_data as the reference server compiled it: the same names in the same order as its dump.
    #[test]
    fn battle_data_is_the_reference_table() {
        let flags = crate::script::battle_flags::BATTLE_FLAGS;
        assert_eq!(flags.len(), N);
        for i in 0..N {
            assert_eq!(BATTLE_DATA[i].name, flags[i].0, "line {i}");
            let d = &BATTLE_DATA[i];
            assert!(d.min <= d.conf && d.conf <= d.max, "{}: conf value outside its range", d.name);
        }
        // rAthena's own table has three defaults outside their range (battle.cpp:8269 and :8466 `~BL_PC` in
        // [BL_NUL, BL_ALL], :8876 1000 under the minimum 60000): the value an out-of-range battle_set_value falls back to
        let outside: Vec<&str> = BATTLE_DATA.iter().filter(|d| d.defval < d.min || d.defval > d.max).map(|d| d.name).collect();
        assert_eq!(outside, ["skillrange_by_distance", "attack_attr_none", "feature.dynamicnpc_timeout"]);
        assert_eq!(BATTLE_DATA[Bc::AREA_SIZE.0 as usize].name, "area_size");
        assert_eq!(BATTLE_DATA[Bc::FEATURE_AUCTION.0 as usize].name, "feature.auction");
        assert_eq!(BATTLE_DATA[Bc::LOKI_SIT_REGEN_RATE.0 as usize].name, "loki_sit_regen_rate");
        assert_eq!(BATTLE_DATA[Bc::ORIDECON_RESEARCH_FIX.0 as usize].name, "oridecon_research_fix");
    }

    /// battle_config_read of the built-in files (rAthena's conf/battle + the overlay's import, battle_adjust_conf) is
    /// what the reference map-server runs with: every one of the 617 members.
    #[test]
    fn builtin_conf_is_the_reference() {
        let (a, warnings) = read_conf(BUILTIN_IMPORT);
        for i in 0..N {
            assert_eq!(a[i], DEFAULTS[i], "{}", BATTLE_DATA[i].name);
        }
        // what the reference server prints at start: feature.mesitemicon (battle.cpp:9136)
        assert_eq!(warnings.len(), 1, "{warnings:?}");
        assert!(warnings[0].contains("mesitemicon"));
        assert_eq!(stamp_of(&a, &mobdb_defaults()), 0);
    }

    /// An empty import file: rAthena's own rates (the overlay's file is what makes area_size 60, EXP x5 and sit regeneration x25).
    #[test]
    fn empty_import_is_stock_rathena() {
        let (a, _) = read_conf(b"");
        assert_eq!(a[Bc::AREA_SIZE.0 as usize], 14);
        assert_eq!(a[Bc::BASE_EXP_RATE.0 as usize], 100);
        assert_eq!(a[Bc::JOB_EXP_RATE.0 as usize], 100);
        assert_eq!(a[Bc::LOKI_SIT_REGEN_RATE.0 as usize], 1);
        let differ: Vec<&str> = (0..N).filter(|&i| a[i] != DEFAULTS[i]).map(|i| BATTLE_DATA[i].name).collect();
        assert_eq!(differ, ["base_exp_rate", "job_exp_rate", "loki_sit_regen_rate", "area_size"]);
        assert_ne!(stamp_of(&a, &mobdb_defaults()), 0);
    }

    /// battle.cpp:9174-9201: `//` lines, lines without a value, names with other case, later lines win, unknown
    /// names, `import:`; battle.cpp:8927-8930: a value outside the range becomes the DEFAULT (not the conf value).
    #[test]
    fn import_lines() {
        let text = b"// area_size: 99\r\nAREA_SIZE: 30\r\nweapon_produce_rate:10000\npotion_produce_rate: 0x64 trailing\n\
            duel_time_interval: no\nno_such_setting: 1\n area_size: 5\nmax_sp: 50\nmax_aspd: 195\nmax_cart_weight: 9000\n\
            import: conf/other.txt\nitem_auto_get\nitem_auto_get:\nfeature.auction: 1\nmax_walk_speed: 150\nlast_line: 1";
        let (a, w) = read_conf(text);
        let v = |k: Bc| a[k.0 as usize];
        assert_eq!(v(Bc::AREA_SIZE), 30);
        assert_eq!(v(Bc::WEAPON_PRODUCE_RATE), 10000);
        assert_eq!(v(Bc::POTION_PRODUCE_RATE), 100);
        assert_eq!(v(Bc::DUEL_TIME_INTERVAL), 0);
        // max_sp: conf/battle/player.conf says 1000000, battle_data's default is 32500, 50 is under the minimum 100
        assert_eq!(v(Bc::MAX_SP), 32500);
        // battle_adjust_conf: (2000 - 195 * 10) * 2, 9000 * 10, 100 * 150 / 150; feature.auction switched off again
        assert_eq!(v(Bc::MAX_ASPD), 100);
        assert_eq!(v(Bc::MAX_CART_WEIGHT), 90000);
        assert_eq!(v(Bc::MAX_WALK_SPEED), 100);
        assert_eq!(v(Bc::FEATURE_AUCTION), 0);
        assert_eq!(v(Bc::ITEM_AUTO_GET), 0);
        let text = w.join("\n");
        assert!(text.contains("Unknown setting 'no_such_setting'"), "{text}");
        assert!(text.contains("Unknown setting ' area_size'"), "{text}");
        assert!(text.contains("Unknown setting 'last_line'"), "{text}");
        assert!(text.contains("Value for setting 'max_sp': 50 is invalid (min:100 max:1000000000)! Defaulting to 32500..."), "{text}");
        assert!(text.contains("File not found: conf/other.txt"), "{text}");
        assert!(text.contains("feature.auction is enabled but it is not available"), "{text}");
        assert_eq!(w.len(), 8, "{text}");
    }

    /// fgets(line, 1024): a line longer than 1023 bytes is read in pieces, each scanned as a line of its own.
    #[test]
    fn long_lines_are_split() {
        let mut text = vec![b' '; 1020];
        text.extend_from_slice(b"area_size: 30\n");
        // piece 1 is 1023 bytes: 1020 spaces + "are" (no colon: skipped); piece 2 is "a_size: 30\n" (unknown name)
        let (a, w) = read_conf(&text);
        assert_eq!(a[Bc::AREA_SIZE.0 as usize], 14);
        assert_eq!(w.iter().filter(|w| w.contains("Unknown setting 'a_size'")).count(), 1, "{w:?}");
    }

    /// strlib.cpp:239 config_switch and glibc's strtol(str, nullptr, 0).
    #[test]
    fn config_switch_values() {
        for (s, v) in [
            ("on", 1), ("YES", 1), ("oui", 1), ("ja", 1), ("si", 1), ("Sim", 1), ("off", 0), ("No", 0), ("non", 0), ("nein", 0), ("nao", 0),
            ("30", 30), ("-5", -5), ("+7", 7), ("0x1F", 31), ("0X1f", 31), ("010", 8), ("0", 0), ("09", 0), ("0x", 0), ("0xg", 0),
            ("12abc", 12), ("abc", 0), ("", 0), ("  42", 42), ("2147483647", i32::MAX), ("2147483648", i32::MIN),
            ("4294967297", 1), ("99999999999999999999", -1), ("-99999999999999999999", 0), ("yes1", 0), ("onn", 0),
        ] {
            assert_eq!(config_switch(s.as_bytes()), v, "{s:?}");
        }
    }

    /// The stamp: 0 for the built-in state only; the same state gives the same stamp whatever the order of changes;
    /// the mob_db copy is part of it.
    #[test]
    fn stamps() {
        let md = mobdb_defaults();
        assert_eq!(md[..3], [500, 500, 100]);
        assert_eq!((mobdb_at(&md, Bc::ITEM_RATE_COMMON), mobdb_at(&md, Bc::ITEM_DROP_CARD_MAX), mobdb_at(&md, Bc::ITEM_LOGARITHMIC_DROPS)), (100, 10000, 0));
        assert_eq!(stamp_of(&DEFAULTS, &md), 0);
        let mut a = DEFAULTS;
        a[Bc::AREA_SIZE.0 as usize] = 30;
        let s1 = stamp_of(&a, &md);
        a[Bc::WEAPON_PRODUCE_RATE.0 as usize] = 10000;
        let s2 = stamp_of(&a, &md);
        let mut b = DEFAULTS;
        b[Bc::WEAPON_PRODUCE_RATE.0 as usize] = 10000;
        b[Bc::AREA_SIZE.0 as usize] = 30;
        assert!(s1 != 0 && s2 != 0 && s1 != s2);
        assert_eq!(stamp_of(&b, &md), s2);
        b[Bc::AREA_SIZE.0 as usize] = 60;
        b[Bc::WEAPON_PRODUCE_RATE.0 as usize] = 100;
        assert_eq!(stamp_of(&b, &md), 0);
        // base_exp_rate set to 100 and mob_db not reloaded yet / reloaded: two states
        b[Bc::BASE_EXP_RATE.0 as usize] = 100;
        let mut reloaded = md;
        reloaded[0] = 100;
        let s3 = stamp_of(&b, &md);
        let s4 = stamp_of(&b, &reloaded);
        assert!(s3 != 0 && s4 != 0 && s3 != s4);
        assert_ne!(stamp_of(&DEFAULTS, &reloaded), 0);
    }

    /// The accessor reads the built-in table until a member is set, and battle_get_value finds names as strcmpi does.
    #[test]
    fn accessor() {
        reset_for_test();
        assert_eq!(get(Bc::AREA_SIZE), 60);
        assert_eq!(get(Bc::BASE_EXP_RATE), 500);
        assert_eq!(battle_get_value("Area_Size"), 60);
        assert_eq!(battle_get_value("no_such_setting"), 0);
        set_for_test(Bc::AREA_SIZE, 30);
        assert_eq!(get(Bc::AREA_SIZE), 30);
        assert_eq!(battle_get_value("area_size"), 30);
        reset_for_test();
        assert_eq!(get(Bc::AREA_SIZE), 60);
        // fame_refine_lv1 is listed twice (battle.cpp:8732-8733): the name is found at its first line
        assert_eq!(find("fame_refine_lv1"), Some(Bc::FAME_REFINE_LV1.0 as usize));
        assert_eq!(BATTLE_DATA[Bc::FAME_REFINE_LV1.0 as usize + 1].name, "fame_refine_lv1");
    }

    /// Every converted setting is listed once.
    #[test]
    fn converted_list() {
        let mut seen = std::collections::BTreeSet::new();
        for k in converted::CONVERTED {
            assert!(seen.insert(k.0), "{} listed twice", BATTLE_DATA[k.0 as usize].name);
        }
    }
}
