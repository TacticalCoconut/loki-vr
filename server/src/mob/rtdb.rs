//! Run-time changes to rAthena's mob_db and to item_data's monster search data. rAthena edits its in-memory
//! databases: addmonsterdrop / delmonsterdrop (script.cpp) change a monster's `dropitem`, mob_clone_spawn (mob.cpp)
//! puts a player clone into mob_db (ids MOB_CLONE_START..MOB_CLONE_END) and mob_clone_delete erases it, and
//! mob_reload_itemmob_data rewrites every item's `mob[MAX_SEARCH]` list. The module's databases are static
//! (db/mobs.rs), so these changes are rows here, and `find` returns the entry as rAthena would see it.
//!
//! The rows are the truth; a `&'static MobDb` for a changed entry is built from its row and cached per row content
//! (a reducer that fails rolls its row back, and the cache then simply holds the old content's entry too).

use std::cell::RefCell;
use std::collections::HashMap;

use spacetimedb::{table, SpacetimeType, Table};

use crate::db::mobs::{self, MobDb, MobDrop, MOB_CLONE_END, MOB_CLONE_START};
use crate::status::data::StatusData;
use crate::unit::ViewData;
use crate::battle_config::{Bc, MobDbConf};
use crate::world::Ctx;

/// itemdb.hpp MAX_SEARCH: the length of item_data::mob[].
pub const MAX_SEARCH: usize = 10;

/// A monster drop as addmonsterdrop writes it (mob.hpp s_mob_drop).
#[derive(SpacetimeType, Clone, Debug, PartialEq, Eq)]
pub struct RtDrop {
    pub nameid: u32,
    pub rate: u32,
    pub randomopt_group: u16,
    pub steal_protected: bool,
}

/// What mob_clone_spawn writes into a new s_mob_db (the rest keeps s_mob_db's defaults).
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct CloneDb {
    /// sd->status.name: the entry's sprite, name and jname.
    pub name: String,
    /// status_get_lv(sd).
    pub lv: u16,
    /// sd->base_status with the clone's ATK / mode / HP / SP changes.
    pub status: StatusData,
    /// sd->vd.
    pub vd: ViewData,
    /// sd->sc.option.
    pub option: u32,
    /// db->skill: the skills mob_clone_spawn takes from the player's tree (mob/skill.rs mob_clone_skills).
    pub skill: Vec<CloneSkill>,
}

/// mob.hpp struct s_mob_skill as a row value (db/mobs.rs MobSkill), for a clone's skill list.
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct CloneSkill {
    pub state: i32,
    pub skill_id: u16,
    pub skill_lv: u16,
    pub permillage: i16,
    pub casttime: i32,
    pub delay: i32,
    pub cancel: i16,
    pub cond1: i16,
    pub cond2: i64,
    pub target: i16,
    /// val[5].
    pub val: Vec<i32>,
    pub emotion: i16,
    pub msg_id: u16,
}

impl From<&mobs::MobSkill> for CloneSkill {
    fn from(s: &mobs::MobSkill) -> Self {
        CloneSkill {
            state: s.state,
            skill_id: s.skill_id,
            skill_lv: s.skill_lv,
            permillage: s.permillage,
            casttime: s.casttime,
            delay: s.delay,
            cancel: s.cancel,
            cond1: s.cond1,
            cond2: s.cond2,
            target: s.target,
            val: s.val.to_vec(),
            emotion: s.emotion,
            msg_id: s.msg_id,
        }
    }
}

impl From<&CloneSkill> for mobs::MobSkill {
    fn from(s: &CloneSkill) -> Self {
        let mut val = [0i32; 5];
        for (d, v) in val.iter_mut().zip(s.val.iter()) {
            *d = *v;
        }
        mobs::MobSkill {
            state: s.state,
            skill_id: s.skill_id,
            skill_lv: s.skill_lv,
            permillage: s.permillage,
            casttime: s.casttime,
            delay: s.delay,
            cancel: s.cancel,
            cond1: s.cond1,
            cond2: s.cond2,
            target: s.target,
            val,
            emotion: s.emotion,
            msg_id: s.msg_id,
        }
    }
}

/// One changed mob_db entry: a clone (`clone` set), and/or a monster whose drop list a script changed (`drops`).
#[table(accessor = mob_db_rt)]
#[derive(Clone, Debug, PartialEq)]
pub struct MobDbRt {
    #[primary_key]
    pub mob_id: u32,
    pub clone: Option<CloneDb>,
    pub drops: Option<Vec<RtDrop>>,
}

/// item_data::mob[] (chance, mob id) of an item after mob_reload_itemmob_data changed it: MAX_SEARCH slots, empty
/// ones (0, 0).
#[table(accessor = item_mob_rt)]
#[derive(Clone, Debug, PartialEq)]
pub struct ItemMobRt {
    #[primary_key]
    pub nameid: u32,
    pub chance: Vec<i32>,
    pub mob_id: Vec<i32>,
}

/// What a built entry depends on besides the static database: the mob_db_rt row (None: no row), the view range (a
/// clone's range2 / range3) and the settings mob_db was last loaded with (battle_config MOBDB_MEMBERS).
type Key = (Option<MobDbRt>, i16, MobDbConf);

thread_local! {
    /// Built entries by what they were built from.
    static CACHE: RefCell<HashMap<u32, Vec<(Key, &'static MobDb)>>> = RefCell::new(HashMap::new());
}

/// mob_db.find(mob_id) as rAthena sees it now.
pub fn find(c: &Ctx, mob_id: u32) -> Option<&'static MobDb> {
    let conf = crate::battle_config::mobdb();
    let row = c.db().mob_db_rt().mob_id().find(mob_id);
    if row.is_none() && conf == *mobs::mob_db().loaded_with() {
        // nothing changed this entry: the embedded one, as datagen loaded it
        return mobs::mob_db().find(mob_id);
    }
    let key: Key = (row, crate::conf::area_size(), conf);
    if let Some(hit) = CACHE.with(|cache| cache.borrow().get(&mob_id).and_then(|v| v.iter().find(|(k, _)| *k == key).map(|(_, d)| *d))) {
        return Some(hit);
    }
    let built = match &key.0 {
        Some(row) => build(row, &conf)?,
        None => loaded(mobs::mob_db().find(mob_id)?.clone(), &conf),
    };
    let leaked: &'static MobDb = Box::leak(Box::new(built));
    CACHE.with(|cache| cache.borrow_mut().entry(mob_id).or_default().push((key, leaked)));
    Some(leaked)
}

/// mmo.hpp MAX_EXP (INT64_MAX).
const MAX_EXP: i64 = i64::MAX;

/// mob.cpp:5077 `static_cast<t_exp>(cap_value((double)exp * (double)battle_config.base_exp_rate / 100., 0, MAX_EXP))`
/// (the same line for job_exp_rate :5086 and mvp_exp_rate :5095). cap_value's result is a double here, so the cap
/// itself is (double)INT64_MAX = 2^63.
pub(crate) fn rate_exp(raw: u64, rate: i32) -> u64 {
    let v = raw as f64 * rate as f64 / 100.0;
    if v >= MAX_EXP as f64 {
        MAX_EXP as f64 as u64
    } else if v <= 0.0 {
        0
    } else {
        v as u64
    }
}

/// mob.cpp:4808 mob_drop_adjust: the logarithmic equation or the linear one, in doubles; capped to
/// [rate_min, rate_max]; `(uint32)`.
pub(crate) fn mob_drop_adjust(baserate: i32, rate_adjust: i32, rate_min: u16, rate_max: u16, logarithmic_drops: bool) -> u32 {
    let mut rate = baserate as f64;
    if logarithmic_drops && rate_adjust > 0 && rate_adjust != 100 && baserate > 0 {
        // Logarithmic drops equation by Ishizu-Chan: Droprate(x,y) = x * (5 - log(x)) ^ (ln(y) / ln(5))
        rate = rate * (5.0 - rate.log10()).powf((rate_adjust as f64 / 100.0).ln() / 5.0f64.ln()) + 0.5;
    } else {
        // Classical linear rate adjustment.
        rate = rate * rate_adjust as f64 / 100.0;
    }
    // cap_value(rate, rate_min, rate_max): (a >= max) ? max : (a <= min) ? min : a
    let capped = if rate >= rate_max as f64 {
        rate_max as f64
    } else if rate <= rate_min as f64 {
        rate_min as f64
    } else {
        rate
    };
    capped as u32
}

/// mob.cpp:6773 mob_drop_ratio_adjust for one monster: its MVP drops and drops (as the files gave them) under `conf`.
/// Entries without an item or a rate, and entries whose rate adjustment is 0, are removed (mob.cpp:6785-6798,
/// :6821-6824, :6887-6891). The item's type is the item database's own (itemdb_search: the dummy item for an unknown
/// id), not a setiteminfo edit: every module instance must make the same list.
///
/// Not here: the item's side (item_data::maxchance and mob[], mob.cpp:6803-6815, :6895-6913). mob_reload inserts the
/// new drops into mob[] (atcommand_admin.rs item_mob_insert_drops); maxchance keeps its start-up value.
pub(crate) fn mob_drop_ratio_adjust(db: &MobDb, raw_mvp: &[MobDrop], raw_drop: &[MobDrop], conf: &MobDbConf) -> (Vec<MobDrop>, Vec<MobDrop>) {
    use crate::db::items::{self as itemdb, IT_ARMOR, IT_CARD, IT_CASH, IT_HEALING, IT_PETARMOR, IT_USABLE, IT_WEAPON};
    let at = |k: Bc| crate::battle_config::mobdb_at(conf, k);
    let logarithmic = at(Bc::ITEM_LOGARITHMIC_DROPS) != 0;
    // mob.cpp:4830 item_dropratio_adjust: an entry of mob_item_ratio for every monster, or for this one
    let ratio = |nameid: u32, rate_adjust: i32| -> i32 {
        match mobs::mob_db().item_ratio(nameid) {
            Some(r) if r.mob_ids.is_empty() || r.mob_ids.contains(&(db.id as u16)) => r.drop_ratio as i32,
            _ => rate_adjust,
        }
    };
    let mut mvpitem = Vec::new();
    for entry in raw_mvp {
        if entry.nameid == 0 || entry.rate == 0 {
            continue;
        }
        let rate_adjust = ratio(entry.nameid, at(Bc::ITEM_RATE_MVP));
        // remove the item if the rate of item_dropratio_adjust is 0
        if rate_adjust == 0 {
            continue;
        }
        let rate = mob_drop_adjust(entry.rate as i32, rate_adjust, at(Bc::ITEM_DROP_MVP_MIN) as u16, at(Bc::ITEM_DROP_MVP_MAX) as u16, logarithmic);
        mvpitem.push(MobDrop { rate, ..entry.clone() });
    }
    let mut dropitem = Vec::new();
    let bosstype = db.get_bosstype();
    for entry in raw_drop {
        if entry.nameid == 0 || entry.rate == 0 {
            continue;
        }
        let item_type = itemdb::itemdb_exists_base(entry.nameid).or_else(|| itemdb::itemdb_exists_base(itemdb::ITEMID_DUMMY)).map(|d| d.r#type).unwrap_or(crate::db::items::IT_ETC);
        let mut rate = entry.rate as i32;
        if at(Bc::DROP_RATEINCREASE) != 0 && rate < 5000 {
            rate += 1;
        }
        let (rate_adjust, ratemin, ratemax);
        if db.race2.contains(&crate::mob::RC2_TREASURE) {
            // Treasure box drop rates [Skotlex]
            rate_adjust = at(Bc::ITEM_RATE_TREASURE);
            ratemin = at(Bc::ITEM_DROP_TREASURE_MIN);
            ratemax = at(Bc::ITEM_DROP_TREASURE_MAX);
        } else {
            let is_mvp = bosstype == mobs::BossType::Mvp;
            let is_boss = bosstype == mobs::BossType::MiniBoss;
            let pick = |normal: Bc, boss: Bc, mvp: Bc| at(if is_mvp { mvp } else if is_boss { boss } else { normal });
            // Added suport to restrict normal drops of MVP's [Reddozen]
            match item_type {
                IT_HEALING => {
                    rate_adjust = pick(Bc::ITEM_RATE_HEAL, Bc::ITEM_RATE_HEAL_BOSS, Bc::ITEM_RATE_HEAL_MVP);
                    ratemin = at(Bc::ITEM_DROP_HEAL_MIN);
                    ratemax = at(Bc::ITEM_DROP_HEAL_MAX);
                }
                IT_USABLE | IT_CASH => {
                    rate_adjust = pick(Bc::ITEM_RATE_USE, Bc::ITEM_RATE_USE_BOSS, Bc::ITEM_RATE_USE_MVP);
                    ratemin = at(Bc::ITEM_DROP_USE_MIN);
                    ratemax = at(Bc::ITEM_DROP_USE_MAX);
                }
                IT_WEAPON | IT_ARMOR | IT_PETARMOR => {
                    rate_adjust = pick(Bc::ITEM_RATE_EQUIP, Bc::ITEM_RATE_EQUIP_BOSS, Bc::ITEM_RATE_EQUIP_MVP);
                    ratemin = at(Bc::ITEM_DROP_EQUIP_MIN);
                    ratemax = at(Bc::ITEM_DROP_EQUIP_MAX);
                }
                IT_CARD => {
                    rate_adjust = pick(Bc::ITEM_RATE_CARD, Bc::ITEM_RATE_CARD_BOSS, Bc::ITEM_RATE_CARD_MVP);
                    ratemin = at(Bc::ITEM_DROP_CARD_MIN);
                    ratemax = at(Bc::ITEM_DROP_CARD_MAX);
                }
                _ => {
                    rate_adjust = pick(Bc::ITEM_RATE_COMMON, Bc::ITEM_RATE_COMMON_BOSS, Bc::ITEM_RATE_COMMON_MVP);
                    ratemin = at(Bc::ITEM_DROP_COMMON_MIN);
                    ratemax = at(Bc::ITEM_DROP_COMMON_MAX);
                }
            }
        }
        let rate_adjust = ratio(entry.nameid, rate_adjust);
        // remove the item if the rate of item_dropratio_adjust is 0
        if rate_adjust == 0 {
            continue;
        }
        let rate = mob_drop_adjust(rate, rate_adjust, ratemin as u16, ratemax as u16, logarithmic);
        dropitem.push(MobDrop { rate, ..entry.clone() });
    }
    (mvpitem, dropitem)
}

/// A mob_db entry as mob_load makes it under `conf` (battle_config MOBDB_MEMBERS at the last mob_load): the node's
/// own EXP values (the dataset's `*_raw`) rated again (MobDatabase::parseBodyNode) and the drop lists adjusted again
/// (mob_drop_ratio_adjust). The embedded entry itself when the dataset was made with the same values.
fn loaded(mut db: MobDb, conf: &MobDbConf) -> MobDb {
    if conf == mobs::mob_db().loaded_with() {
        return db;
    }
    let at = |k: Bc| crate::battle_config::mobdb_at(conf, k);
    db.base_exp = rate_exp(db.base_exp_raw, at(Bc::BASE_EXP_RATE));
    db.job_exp = rate_exp(db.job_exp_raw, at(Bc::JOB_EXP_RATE));
    db.mexp = rate_exp(db.mexp_raw, at(Bc::MVP_EXP_RATE));
    let raw_mvp = if db.mvpitem_raw.is_empty() { db.mvpitem.clone() } else { db.mvpitem_raw.clone() };
    let raw_drop = if db.dropitem_raw.is_empty() { db.dropitem.clone() } else { db.dropitem_raw.clone() };
    let (mvpitem, dropitem) = mob_drop_ratio_adjust(&db, &raw_mvp, &raw_drop, conf);
    db.mvpitem = mvpitem;
    db.dropitem = dropitem;
    loading_finished(&mut db, conf);
    db
}

/// The settings MobDatabase applies to an entry's own values while it loads, under `conf`: mob.cpp:5409-5412 (the
/// DamageMotion node times monster_damage_delay_rate), and MobDatabase::loadingFinished's view_range_rate /
/// chase_range_rate (:5556-5560), monster_active_enable (:5567-5568) and mvp_hp_rate / monster_hp_rate (:5578-5591).
/// Each is applied only when the embedded data was made with the setting's neutral value (100; 1 for
/// monster_active_enable), because only then is the embedded value the file's own; otherwise the entry keeps what
/// datagen baked. status_calc_misc (:5576, status.cpp:2600-2765) reads none of the fields changed here.
pub(crate) fn loading_finished(db: &mut MobDb, conf: &MobDbConf) {
    let base = mobs::mob_db().loaded_with();
    let at = |k: Bc| crate::battle_config::mobdb_at(conf, k);
    let made_with = |k: Bc| crate::battle_config::mobdb_at(base, k);
    // uint16 speed; `speed = speed * battle_config.monster_damage_delay_rate / 100` (int arithmetic, stored as uint16)
    if made_with(Bc::MONSTER_DAMAGE_DELAY_RATE) == 100 && at(Bc::MONSTER_DAMAGE_DELAY_RATE) != 100 {
        db.status.dmotion = ((db.status.dmotion as i32).wrapping_mul(at(Bc::MONSTER_DAMAGE_DELAY_RATE)) / 100) as u16;
    }
    if made_with(Bc::VIEW_RANGE_RATE) == 100 && made_with(Bc::CHASE_RANGE_RATE) == 100 {
        if at(Bc::VIEW_RANGE_RATE) != 100 {
            db.range2 = 1.max((db.range2 as i32).wrapping_mul(at(Bc::VIEW_RANGE_RATE)) / 100) as u16;
        }
        if at(Bc::CHASE_RANGE_RATE) != 100 {
            db.range3 = (db.range2 as i32).max((db.range3 as i32).wrapping_mul(at(Bc::CHASE_RANGE_RATE)) / 100) as u16;
        }
    }
    if made_with(Bc::MONSTER_ACTIVE_ENABLE) == 1 && at(Bc::MONSTER_ACTIVE_ENABLE) == 0 {
        db.status.mode &= !mobs::MD_AGGRESSIVE;
    }
    // Now that we know if it is a MVP or not, apply battle_config modifiers [Skotlex]
    if made_with(Bc::MVP_HP_RATE) == 100 && made_with(Bc::MONSTER_HP_RATE) == 100 {
        let mut maxhp = db.status.max_hp as f64;
        if db.get_bosstype() == mobs::BossType::Mvp {
            if at(Bc::MVP_HP_RATE) != 100 {
                maxhp = maxhp * at(Bc::MVP_HP_RATE) as f64 / 100.;
            }
        } else if at(Bc::MONSTER_HP_RATE) != 100 {
            maxhp = maxhp * at(Bc::MONSTER_HP_RATE) as f64 / 100.;
        }
        // cap_value(static_cast<uint32>(maxhp), 1, UINT32_MAX)
        db.status.max_hp = (maxhp as u32).max(1);
        db.status.hp = db.status.max_hp;
    }
}

/// mob_db.exists(mob_id).
pub fn exists(c: &Ctx, mob_id: u32) -> bool {
    find(c, mob_id).is_some()
}

fn build(row: &MobDbRt, conf: &MobDbConf) -> Option<MobDb> {
    let mut db = match &row.clone {
        Some(cl) => {
            let mut db = MobDb { id: row.mob_id, ..Default::default() };
            db.sprite = cl.name.clone();
            db.name = cl.name.clone();
            db.jname = cl.name.clone();
            db.lv = cl.lv;
            db.status = status_to_db(&cl.status);
            db.vd = view_to_db(&cl.vd);
            db.base_exp = 1;
            db.job_exp = 1;
            // mob.cpp:4626-4627 mob_clone_spawn: `db->range2=AREA_SIZE` / `db->range3=AREA_SIZE` when the clone is made.
            // Deliberately different since area_size is a run-time setting: the row does not keep the value of that
            // moment, so a clone alive across a change of area_size follows the new value (every module instance
            // the same: the cache is keyed by it), where rAthena's clone keeps the old one until it is gone.
            db.range2 = crate::conf::area_size() as u16; // Let them have the same view-range as players.
            db.range3 = crate::conf::area_size() as u16; // Min chase of a screen.
            db.option = cl.option;
            // s_mob_db's constructor defaults
            db.damagetaken = 100;
            // db->skill: the clone's skills from the player's tree
            db.skill = cl.skill.iter().map(mobs::MobSkill::from).collect();
            db
        }
        None => loaded(mobs::mob_db().find(row.mob_id)?.clone(), conf),
    };
    if let Some(drops) = &row.drops {
        db.dropitem = drops.iter().map(|d| MobDrop { nameid: d.nameid, rate: d.rate, randomopt_group: d.randomopt_group, steal_protected: d.steal_protected }).collect();
    }
    Some(db)
}

/// mob.cpp mob_is_clone: the id when it is in the clone range and defined.
pub fn mob_is_clone(c: &Ctx, mob_id: i32) -> i32 {
    if !(MOB_CLONE_START..=MOB_CLONE_END).contains(&mob_id) {
        return 0;
    }
    if !exists(c, mob_id as u32) {
        return 0;
    }
    mob_id
}

/// mob.cpp mob_get_viewdata (with clones).
pub fn mob_get_viewdata(c: &Ctx, mob_id: i32) -> Option<&'static mobs::ViewData> {
    if mob_id < 0 {
        return None;
    }
    find(c, mob_id as u32).map(|m| &m.vd)
}

/// A monster's current drop list (mob->dropitem).
pub fn dropitem(c: &Ctx, mob_id: u32) -> Vec<MobDrop> {
    find(c, mob_id).map(|m| m.dropitem.clone()).unwrap_or_default()
}

/// Replaces a monster's drop list (addmonsterdrop / delmonsterdrop edit mob->dropitem in place).
pub fn set_dropitem(c: &Ctx, mob_id: u32, drops: &[MobDrop]) {
    let list: Vec<RtDrop> = drops.iter().map(|d| RtDrop { nameid: d.nameid, rate: d.rate, randomopt_group: d.randomopt_group, steal_protected: d.steal_protected }).collect();
    match c.db().mob_db_rt().mob_id().find(mob_id) {
        Some(mut r) => {
            r.drops = Some(list);
            c.db().mob_db_rt().mob_id().update(r);
        }
        None => {
            c.db().mob_db_rt().insert(MobDbRt { mob_id, clone: None, drops: Some(list) });
        }
    }
}

/// mob_db.put(mob_id, clone entry) (mob_clone_spawn).
pub fn put_clone(c: &Ctx, mob_id: u32, cl: CloneDb) {
    if c.db().mob_db_rt().mob_id().find(mob_id).is_some() {
        c.db().mob_db_rt().mob_id().delete(mob_id);
    }
    c.db().mob_db_rt().insert(MobDbRt { mob_id, clone: Some(cl), drops: None });
}

/// mob_db.erase(mob_id) of a clone (mob_clone_delete).
pub fn erase_clone(c: &Ctx, mob_id: u32) {
    if c.db().mob_db_rt().mob_id().find(mob_id).map(|r| r.clone.is_some()).unwrap_or(false) {
        c.db().mob_db_rt().mob_id().delete(mob_id);
    }
}

// ---------------------------------------------------------------------------------------------- item_data mob[]

/// item_data::mob[] of an item: MAX_SEARCH (chance, mob id) slots.
pub fn item_mob(c: &Ctx, nameid: u32) -> [(i32, i32); MAX_SEARCH] {
    let mut out = [(0, 0); MAX_SEARCH];
    if let Some(r) = c.db().item_mob_rt().nameid().find(nameid) {
        for (k, slot) in out.iter_mut().enumerate() {
            *slot = (r.chance.get(k).copied().unwrap_or(0), r.mob_id.get(k).copied().unwrap_or(0));
        }
        return out;
    }
    if let Some(info) = mobs::mob_item_drop_info().find(nameid) {
        for (k, &(chance, id)) in info.mob.iter().take(MAX_SEARCH).enumerate() {
            out[k] = (chance, id);
        }
    }
    out
}

/// mob.cpp mob_reload_itemmob_data: every monster's drops (clones excluded), in mob_db's iteration order, go into
/// their item's mob[] list (highest chance first). Unlike the load-time pass (mob_drop_ratio_adjust) it does not
/// skip treasure chests, and it never removes an entry, so a monster can end up twice in a list, as in rAthena.
pub fn mob_reload_itemmob_data(c: &Ctx) {
    let mut work: HashMap<u32, [(i32, i32); MAX_SEARCH]> = HashMap::new();
    let mut order: Vec<u32> = Vec::new();
    for mob in crate::atcommand_atcmd::admin::mob_db_iter(c) {
        // (clones are never in the static database: `if( mob_is_clone( pair.first ) ) continue;`)
        let drops = match c.db().mob_db_rt().mob_id().find(mob.id).and_then(|r| r.drops) {
            Some(d) => d.into_iter().map(|d| (d.nameid, d.rate)).collect::<Vec<_>>(),
            None => mob.dropitem.iter().map(|d| (d.nameid, d.rate)).collect(),
        };
        for (nameid, rate) in drops {
            if nameid == 0 {
                continue;
            }
            // itemdb_search: an unknown id is the dummy item
            let key = if crate::db::items::itemdb_exists(nameid).is_some() { nameid } else { crate::db::items::ITEMID_DUMMY };
            let arr = work.entry(key).or_insert_with(|| {
                order.push(key);
                item_mob(c, key)
            });
            let Some(k) = (0..MAX_SEARCH).find(|&k| (arr[k].0 as i64) <= rate as i64) else { continue };
            if arr[k].1 != mob.id as i32 {
                for j in (k + 1..MAX_SEARCH).rev() {
                    arr[j] = arr[j - 1];
                }
            }
            arr[k] = (rate as i32, mob.id as i32);
        }
    }
    for nameid in order {
        let arr = work[&nameid];
        if arr == item_mob(c, nameid) {
            continue;
        }
        let row = ItemMobRt { nameid, chance: arr.iter().map(|s| s.0).collect(), mob_id: arr.iter().map(|s| s.1).collect() };
        if c.db().item_mob_rt().nameid().find(nameid).is_some() {
            c.db().item_mob_rt().nameid().update(row);
        } else {
            c.db().item_mob_rt().insert(row);
        }
    }
}

// ---------------------------------------------------------------------------------------------- conversions

/// A runtime status_data as the database's (the reverse of status::status_from_db).
pub fn status_to_db(s: &StatusData) -> mobs::StatusData {
    let w = |a: &crate::status::data::WeaponAtk| mobs::WeaponAtk { atk: a.atk, atk2: a.atk2, range: a.range, ele: a.ele, matk: a.matk, wlv: a.wlv };
    mobs::StatusData {
        hp: s.hp,
        sp: s.sp,
        ap: s.ap,
        max_hp: s.max_hp,
        max_sp: s.max_sp,
        max_ap: s.max_ap,
        str: s.str,
        agi: s.agi,
        vit: s.vit,
        int_: s.int_,
        dex: s.dex,
        luk: s.luk,
        pow: s.pow,
        sta: s.sta,
        wis: s.wis,
        spl: s.spl,
        con: s.con,
        crt: s.crt,
        eatk: s.eatk,
        batk: s.batk,
        watk: s.watk,
        watk2: s.watk2,
        matk_min: s.matk_min,
        matk_max: s.matk_max,
        speed: s.speed,
        amotion: s.amotion,
        clientamotion: s.clientamotion,
        adelay: s.adelay,
        dmotion: s.dmotion,
        mode: s.mode,
        hit: s.hit,
        flee: s.flee,
        cri: s.cri,
        flee2: s.flee2,
        def2: s.def2,
        mdef2: s.mdef2,
        aspd_rate2: s.aspd_rate2,
        aspd_rate: s.aspd_rate,
        patk: s.patk,
        smatk: s.smatk,
        res: s.res,
        mres: s.mres,
        hplus: s.hplus,
        crate_: s.crate_,
        def: s.def,
        mdef: s.mdef,
        def_ele: s.def_ele,
        ele_lv: s.ele_lv,
        size: s.size,
        race: s.race,
        class_: s.class_,
        rhw: w(&s.rhw),
        lhw: w(&s.lhw),
    }
}

/// A unit's view_data as the database's look array (the reverse of status::view_from_db).
pub fn view_to_db(vd: &ViewData) -> mobs::ViewData {
    use crate::db::mobs::{LOOK_BASE, LOOK_BODY, LOOK_BODY2, LOOK_CLOTHES_COLOR, LOOK_HAIR, LOOK_HAIR_COLOR, LOOK_HEAD_BOTTOM, LOOK_HEAD_MID, LOOK_HEAD_TOP, LOOK_ROBE, LOOK_SHIELD, LOOK_SHOES, LOOK_WEAPON};
    let mut out = mobs::ViewData::default();
    out.look[LOOK_BASE] = vd.class;
    out.look[LOOK_HAIR] = vd.hair;
    out.look[LOOK_WEAPON] = vd.weapon;
    out.look[LOOK_HEAD_BOTTOM] = vd.head_bottom;
    out.look[LOOK_HEAD_TOP] = vd.head_top;
    out.look[LOOK_HEAD_MID] = vd.head_mid;
    out.look[LOOK_HAIR_COLOR] = vd.hair_color;
    out.look[LOOK_CLOTHES_COLOR] = vd.clothes_color;
    out.look[LOOK_SHIELD] = vd.shield;
    out.look[LOOK_SHOES] = vd.shoes;
    out.look[LOOK_BODY] = vd.body;
    out.look[LOOK_ROBE] = vd.robe;
    out.look[LOOK_BODY2] = vd.body2;
    out.sex = vd.sex as i8;
    out.dead_sit = vd.dead_sit;
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::battle_config::MOBDB_MEMBERS;

    fn with(conf: &MobDbConf, changes: &[(Bc, i32)]) -> MobDbConf {
        let mut c = *conf;
        for (k, v) in changes {
            c[MOBDB_MEMBERS.iter().position(|m| m == k).expect("a mob_db member")] = *v;
        }
        c
    }

    /// mob.cpp:5077-5095: the dataset's raw EXP times the rates datagen loaded mob_db with is the dataset's EXP, for
    /// every monster; under other rates the same line gives the reloaded value.
    #[test]
    fn exp_rates_reproduce_the_dataset() {
        crate::battle_config::reset_for_test();
        let db = mobs::mob_db();
        let conf = *db.loaded_with();
        // the embedded data was made with the built-in battle_config (so a database nobody changed takes the
        // embedded entries as they are)
        assert_eq!(conf, crate::battle_config::mobdb());
        let rates = [conf[0], conf[1], conf[2]];
        assert_eq!(rates, [500, 500, 100]);
        let (mut with_base, mut with_mvp) = (0, 0);
        for m in db.iter() {
            assert_eq!(rate_exp(m.base_exp_raw, rates[0]), m.base_exp, "{} base", m.id);
            assert_eq!(rate_exp(m.job_exp_raw, rates[1]), m.job_exp, "{} job", m.id);
            assert_eq!(rate_exp(m.mexp_raw, rates[2]), m.mexp, "{} mvp", m.id);
            with_base += (m.base_exp_raw != 0) as u32;
            with_mvp += (m.mexp_raw != 0) as u32;
        }
        assert!(with_base > 1000 && with_mvp > 50, "{with_base} {with_mvp}");
        // Poring: the node's own BaseExp / JobExp at rAthena's stock rates
        let poring = db.find(1002).expect("Poring");
        let stock = loaded(poring.clone(), &with(&conf, &[(Bc::BASE_EXP_RATE, 100), (Bc::JOB_EXP_RATE, 100)]));
        assert_eq!((stock.base_exp, stock.job_exp), (poring.base_exp_raw, poring.job_exp_raw));
        assert_eq!(poring.base_exp, poring.base_exp_raw * 5);
        assert_eq!((&stock.dropitem, &stock.mvpitem), (&poring.dropitem, &poring.mvpitem));
        // the double arithmetic and the cap
        assert_eq!(rate_exp(27, 150), 40);
        assert_eq!(rate_exp(1, 50), 0);
        assert_eq!(rate_exp(27, 0), 0);
        assert_eq!(rate_exp(u64::MAX, i32::MAX), 1 << 63);
    }

    /// mob.cpp:6773 mob_drop_ratio_adjust: under the settings datagen used, the port makes the dataset's own lists
    /// from the raw ones, for every monster (datagen's Python port is the one the parity suites verify against
    /// rAthena); raw lists are kept exactly where the adjustment changed something.
    #[test]
    fn drop_adjust_reproduces_the_dataset() {
        let db = mobs::mob_db();
        let conf = *db.loaded_with();
        let (mut drops, mut changed) = (0, 0);
        for m in db.iter() {
            let raw_mvp = if m.mvpitem_raw.is_empty() { &m.mvpitem } else { &m.mvpitem_raw };
            let raw_drop = if m.dropitem_raw.is_empty() { &m.dropitem } else { &m.dropitem_raw };
            let (mvpitem, dropitem) = mob_drop_ratio_adjust(m, raw_mvp, raw_drop, &conf);
            assert_eq!(mvpitem, m.mvpitem, "{} MVP drops", m.id);
            assert_eq!(dropitem, m.dropitem, "{} drops", m.id);
            assert!(m.dropitem_raw.is_empty() || m.dropitem_raw != m.dropitem, "{}", m.id);
            assert!(m.mvpitem_raw.is_empty() || m.mvpitem_raw != m.mvpitem, "{}", m.id);
            drops += m.dropitem.len();
            changed += (!m.dropitem_raw.is_empty() || !m.mvpitem_raw.is_empty()) as u32;
        }
        assert!(drops > 5000, "{drops}");
        // (how many monsters the stock adjustment changes at all: entries without a rate, mob_item_ratio.yml)
        let _ = changed;
    }

    /// mob.cpp:6773-6913 under other settings, on Poring (a normal monster: Jellopy 7000, Knife [4] 100, Sticky Mucus
    /// 400, Apple 1000, Empty Bottle 1500, Apple 150, Unripe Apple 20, Poring Card 1) and Osiris (an MVP).
    #[test]
    fn drop_adjust_under_other_rates() {
        let db = mobs::mob_db();
        let conf = *db.loaded_with();
        let poring = db.find(1002).expect("Poring");
        assert!(poring.dropitem_raw.is_empty());
        let rates = |m: &MobDb| -> Vec<(u32, u32)> { m.dropitem.iter().map(|d| (d.nameid, d.rate)).collect() };
        let before = rates(poring);
        let kind = |nameid: u32| crate::db::items::itemdb_exists_base(nameid).map(|d| d.r#type).unwrap_or(3);
        // common x2, capped at item_drop_common_max; the other kinds as they were
        let x2 = loaded(poring.clone(), &with(&conf, &[(Bc::ITEM_RATE_COMMON, 200)]));
        for ((id, old), (id2, new)) in before.iter().zip(rates(&x2)) {
            assert_eq!(*id, id2);
            let common = !matches!(kind(*id), 0 | 2 | 4 | 5 | 6 | 8 | 18);
            assert_eq!(new, if common { (*old * 2).min(10000) } else { *old }, "item {id}");
        }
        assert!(before.iter().any(|(id, _)| !matches!(kind(*id), 0 | 2 | 4 | 5 | 6 | 8 | 18)));
        // cards off: the card leaves the list (rate_adjust 0), nothing else moves
        let nocard = loaded(poring.clone(), &with(&conf, &[(Bc::ITEM_RATE_CARD, 0)]));
        let expect: Vec<(u32, u32)> = before.iter().copied().filter(|(id, _)| kind(*id) != 6).collect();
        assert_eq!(rates(&nocard), expect);
        assert!(expect.len() < before.len());
        // a maximum under the rate, a minimum over it, drop_rateincrease (+1 under 5000 before the adjustment)
        let capped = loaded(poring.clone(), &with(&conf, &[(Bc::ITEM_DROP_COMMON_MAX, 500), (Bc::ITEM_DROP_CARD_MIN, 30), (Bc::DROP_RATEINCREASE, 1)]));
        for ((id, old), (_, new)) in before.iter().zip(rates(&capped)) {
            let plus = if *old < 5000 { *old + 1 } else { *old };
            let want = match kind(*id) {
                6 => plus.max(30),
                0 | 2 | 4 | 5 | 8 | 18 => plus,
                _ => plus.min(500),
            };
            assert_eq!(new, want, "item {id}");
        }
        // a normal monster does not read the boss / MVP rates; an MVP reads the MVP ones, and item_rate_mvp for its
        // MVP drops
        let other = loaded(poring.clone(), &with(&conf, &[(Bc::ITEM_RATE_COMMON_BOSS, 300), (Bc::ITEM_RATE_COMMON_MVP, 300), (Bc::ITEM_RATE_MVP, 300), (Bc::ITEM_RATE_TREASURE, 300)]));
        assert_eq!(rates(&other), before);
        let osiris = db.find(1038).expect("Osiris");
        assert_eq!(osiris.get_bosstype(), mobs::BossType::Mvp);
        let o2 = loaded(osiris.clone(), &with(&conf, &[(Bc::ITEM_RATE_COMMON, 300), (Bc::ITEM_RATE_EQUIP_MVP, 50), (Bc::ITEM_RATE_MVP, 200)]));
        for (old, new) in osiris.dropitem.iter().zip(&o2.dropitem) {
            let equip = matches!(kind(old.nameid), 4 | 5 | 8);
            assert_eq!(new.rate, if equip { (old.rate / 2).max(1) } else { old.rate }, "item {}", old.nameid);
        }
        for (old, new) in osiris.mvpitem.iter().zip(&o2.mvpitem) {
            assert_eq!(new.rate, (old.rate * 2).min(10000));
        }
        assert!(!osiris.mvpitem.is_empty());
        // loadingFinished's settings (mob.cpp:5409, :5556-5591): Poring is a normal monster, Osiris an MVP
        assert!(poring.status.mode & mobs::MD_AGGRESSIVE == 0 && osiris.status.mode & mobs::MD_AGGRESSIVE != 0);
        let lf = with(&conf, &[
            (Bc::MONSTER_HP_RATE, 250),
            (Bc::MVP_HP_RATE, 50),
            (Bc::VIEW_RANGE_RATE, 200),
            (Bc::CHASE_RANGE_RATE, 10),
            (Bc::MONSTER_DAMAGE_DELAY_RATE, 50),
            (Bc::MONSTER_ACTIVE_ENABLE, 0),
        ]);
        let (p2, o3) = (loaded(poring.clone(), &lf), loaded(osiris.clone(), &lf));
        assert_eq!((p2.status.max_hp, p2.status.hp), ((poring.status.max_hp as f64 * 2.5) as u32, (poring.status.max_hp as f64 * 2.5) as u32));
        assert_eq!(o3.status.max_hp, osiris.status.max_hp / 2);
        assert_eq!(p2.range2, poring.range2 * 2);
        // the chase range is never under the (new) view range
        assert_eq!(p2.range3, p2.range2.max(poring.range3 / 10));
        assert_eq!(p2.status.dmotion, poring.status.dmotion / 2);
        assert_eq!(o3.status.mode, osiris.status.mode & !mobs::MD_AGGRESSIVE);
        assert_eq!((&p2.dropitem, p2.base_exp), (&poring.dropitem, poring.base_exp));
        // the equations themselves (mob.cpp:4808-4821)
        assert_eq!(mob_drop_adjust(7000, 200, 1, 10000, false), 10000);
        assert_eq!(mob_drop_adjust(1, 50, 1, 10000, false), 1);
        assert_eq!(mob_drop_adjust(1, 50, 0, 10000, false), 0);
        assert_eq!(mob_drop_adjust(333, 150, 1, 10000, false), 499);
        // logarithmic: 100 * (5 - 2) ^ (ln 2 / ln 5) = 160.51, + 0.5 = 161.01; the linear branch for an adjustment of 100
        assert_eq!(mob_drop_adjust(100, 200, 1, 10000, true), 161);
        assert_eq!(mob_drop_adjust(100, 100, 1, 10000, true), 100);
    }
}
