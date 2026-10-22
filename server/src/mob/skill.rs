//! mob.cpp's monster skills (wave D skillcore): mobskill_use (the MSC_* conditions, the MST_* targets, the ground /
//! targeted cast through the skill pipeline, the chat message), mobskill_event, mobskill_delay, mob_getstatus_sub and
//! the condition searches (mob_getfriendhprate, mob_getmasterhpltmaxrate, mob_getfriendstatus and their _sub
//! functions), mob_skill_id2skill_idx, mob_chat_display_message, the skill list mob_clone_spawn builds from a player's
//! skill tree (`mob_clone_skills`), and the cast-end emotion of a monster skill (skill.cpp).
//!
//! The skills' own effects (the NPC_* classes of skills/npc/) are the next wave: the pipeline (skill/unit.rs,
//! skill/castend.rs) takes rAthena's "no impl" path for them. Casting goes through unit_skilluse_id2 /
//! unit_skilluse_pos2 (skill/unit.rs), which may end the cast at once (skill_castend_id / pos): every row read before
//! such a call is read again after it.

use crate::db::mobs::*;
use crate::db::skills::*;
use crate::map::{self, BL_PC};
use crate::status::consts::{SC_COMMON_MAX, SC_COMMON_MIN, SC_NONE};
use crate::status::defs::{BF_LONG, BF_MAGIC, BF_SHORT};
use crate::status::sc::Sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::battle_iface::{battle_check_range, battle_check_target, battle_getenemy, BCT_ENEMY};
use super::unit::unit_counttargeted;
use super::{conf, get, put, MobData, AI_NONE, RUDE_ATTACKED_COUNT};

/// clif.hpp emotion_type ET_SURPRISE / ET_MAX.
pub const ET_SURPRISE: i16 = 0;
pub const ET_MAX: i16 = 88;

/// map_id2bl: any object by id (0 is no object).
fn map_id2bl(c: &Ctx, id: u32) -> Option<Unit> {
    if id == 0 {
        return None;
    }
    unit::get(c, id)
}

/// common/utils.cpp get_percentage(uint32 A, uint32 B): `floor(100 * ((double)A / (double)B))`, ~0U (reported) when
/// B is 0, UINT_MAX when the result is above it. The double arithmetic is kept: get_percentage(29, 100) is 28.
pub fn get_percentage(a: u32, b: u32) -> u32 {
    if b == 0 {
        log::error!("get_percentage: divison by zero! (A={a},B={b})");
        return !0u32;
    }
    let result = 100.0 * (a as f64 / b as f64);
    if result > u32::MAX as f64 {
        log::error!("get_percentage: result percentage too high! (A={a},B={b},result={result})");
        return u32::MAX;
    }
    result.floor() as u32
}

/// md->skilldelay[i]. rAthena's array holds MAX_MOBSKILL entries, but a clone can get MAX_MOBSKILL + 1 skills
/// (mob_clone_spawn pushes a support skill twice after the size check): entry MAX_MOBSKILL then reads and writes the
/// first 8 bytes of md->npc_event. Here that entry is 0 until mobskill_delay sets it (an empty npc_event reads 0; the
/// event name is not overwritten).
fn skilldelay_get(md: &MobData, i: usize) -> Tick {
    md.skilldelay.get(i).copied().unwrap_or(0)
}

fn skilldelay_set(skilldelay: &mut Vec<Tick>, i: usize, v: Tick) {
    if i >= skilldelay.len() {
        skilldelay.resize(i + 1, 0);
    }
    skilldelay[i] = v;
}

// ---------------------------------------------------------------------------------------------- lookups

/// mob.cpp mob_skill_id2skill_idx: the index of the first entry of `skill_id` in the monster's skill list, -1 when
/// there is none.
pub fn mob_skill_id2skill_idx(c: &Ctx, mob_id: i32, skill_id: u16) -> i32 {
    let Some(mob) = super::db_find(c, mob_id) else { return -1 };
    if mob.skill.is_empty() {
        return -1;
    }
    mob.skill.iter().position(|s| s.skill_id == skill_id).map(|i| i as i32).unwrap_or(-1)
}

/// mob.cpp mob_getstatus_sub on the monster's status_change `sc`: `type_` SC_NONE looks for any of the common
/// ailments SC_COMMON_MIN..SC_COMMON_MAX, else for that status; the ON conditions want it found, the OFF ones not.
pub fn mob_getstatus_sub(sc: &Sc, condition: i16, type_: i32) -> bool {
    let found = if type_ == SC_NONE {
        // Once an effect was found, break out. [Skotlex]
        (SC_COMMON_MIN..=SC_COMMON_MAX).any(|i| sc.has(i))
    } else {
        sc.has(type_)
    };
    match condition {
        MSC_MYSTATUSON | MSC_FRIENDSTATUSON => found,
        MSC_MYSTATUSOFF | MSC_FRIENDSTATUSOFF => !found,
        _ => false,
    }
}

/// mob.cpp mob_getfriendhprate (+ mob_getfriendhprate_sub): the first non-enemy within 8 cells (all range, block
/// order) whose HP percentage is in [min_rate, max_rate]. Monsters look at monsters, summons (special AI) at players;
/// the monster itself only with monster_ai 0x10.
fn mob_getfriendhprate(c: &Ctx, md: &MobData, mu: &Unit, min_rate: i64, max_rate: i64) -> Option<Unit> {
    // Summoned creatures. [Skotlex]
    let kind = if md.special_ai != AI_NONE { BL_PC } else { map::BL_MOB };
    for bl in map::units_in_range(c, mu.map, mu.x, mu.y, 8, kind) {
        if md.id == bl.id && conf::mob_ai() & 0x10 == 0 {
            continue;
        }
        if battle_check_target(c, mu, &bl, BCT_ENEMY) > 0 {
            continue;
        }
        let st = crate::status::status_get_status_data(c, &bl);
        let rate = get_percentage(st.hp, st.max_hp) as i64;
        if rate >= min_rate && rate <= max_rate {
            return Some(bl); // A friend was found: the other units are skipped.
        }
    }
    None
}

/// mob.cpp mob_getmasterhpltmaxrate: the master when its HP percentage is below `rate`.
fn mob_getmasterhpltmaxrate(c: &Ctx, md: &MobData, rate: i64) -> Option<Unit> {
    if md.master_id > 0 {
        if let Some(bl) = map_id2bl(c, md.master_id) {
            let st = crate::status::status_get_status_data(c, &bl);
            if (get_percentage(st.hp, st.max_hp) as i64) < rate {
                return Some(bl);
            }
        }
    }
    None
}

/// mob.cpp mob_getfriendstatus (+ mob_getfriendstatus_sub): the non-enemy monster within 8 cells (all range) whose
/// status matches cond1 / cond2 (mob_getstatus_sub); the last one met in block order wins.
fn mob_getfriendstatus(c: &Ctx, mmd: &MobData, mu: &Unit, cond1: i64, cond2: i64) -> Option<Unit> {
    let mut fr = None;
    for bl in map::units_in_range(c, mu.map, mu.x, mu.y, 8, map::BL_MOB) {
        if mmd.id == bl.id && conf::mob_ai() & 0x10 == 0 {
            continue;
        }
        if battle_check_target(c, mu, &bl, BCT_ENEMY) > 0 {
            continue;
        }
        let sc = crate::status::sc::load(c, bl.id);
        if mob_getstatus_sub(&sc, cond1 as i16, cond2 as i32) {
            fr = Some(bl);
        }
    }
    fr
}

/// mobskill_use's `map_foreachinallrange(mob_count_sub, md, AREA_SIZE, BL_MOB)` (MSC_MOBNEARBYGT): the monsters
/// mob_count_sub counts around the monster (itself included).
///
/// mob_count_sub reads up to ten monster ids with va_arg until one is 0: with a first id it counts the monsters of
/// those classes, without one ("backward compatibility") every unit. mobskill_use passes no ids at all, so what is
/// read is undefined in C++ and decided by the build. In the reference map-server (g++ 13, x86-64; read from its
/// disassembly: mobskill_use+0x7d1 sets only rdi / rsi / edx / ecx / eax for the call) the ids are r8d, r9d and the
/// words of mobskill_use's stack frame: r8d is battle_config.mob_skill_rate, loaded at the function's entry (rnd()
/// leaves r8 alone, save that the generator's refill every 624 rolls puts its address there; the condition of an
/// earlier entry of the same call may have left another value), r9d is the entry's cond1 (mobskill_use+0x183). The
/// first id is therefore not 0 and a monster counts only when its class is one of the values read, which are not
/// monster ids: the count is 0 unless a stack word happens to equal a class. What is ported is the part that is
/// known, mob_skill_rate and cond1 as the ids (the stack words cannot be); like the level-0 reads of db/skills.rs it
/// follows the reference build, not the intent (counting every monster nearby).
fn mob_count_nearby(c: &Ctx, mu: &Unit, cond1: i16) -> usize {
    let nearby = map::units_in_range(c, mu.map, mu.x, mu.y, crate::conf::area_size(), map::BL_MOB);
    let mobid = [conf::mob_skill_rate(), cond1 as i32];
    if mobid[0] == 0 {
        return nearby.len(); // mob_count_sub without a first id counts every unit
    }
    // md->mob_id is an int16
    nearby.iter().filter(|bl| get(c, bl.id).map(|m| mobid.contains(&(m.mob_id as i16 as i32))).unwrap_or(false)).count()
}

// ---------------------------------------------------------------------------------------------- chat

/// mob.cpp mob_chat_display_message: "<name> : <dialog>" of mob_chat_db entry `msg_id` in the entry's colour to the
/// monster's chat area (AREA_CHAT_WOC); the name stops at its first '#'. False when the entry does not exist.
pub fn mob_chat_display_message(c: &Ctx, md_id: u32, msg_id: u16) -> bool {
    let Some(md) = get(c, md_id) else { return false };
    let Some(u) = unit::get(c, md_id) else { return false };
    chat_display(c, &md, &u, msg_id)
}

fn chat_display(c: &Ctx, md: &MobData, bl: &Unit, msg_id: u16) -> bool {
    let Some(mc) = mob_chat_db().find(msg_id) else { return false };
    let name = crate::net::wire::latin1(&md.name);
    // discard extra name identifier if present [Daegaladh]
    let cut = name.iter().position(|&b| b == b'#').unwrap_or(name.len());
    let mut output = name[..cut].to_vec();
    output.extend_from_slice(b" : ");
    output.extend_from_slice(&crate::net::wire::latin1(&mc.msg));
    crate::clif::messagecolor_target(c, bl, mc.color, &output, true, crate::clif::Target::AreaChatWoc, None);
    true
}

// ---------------------------------------------------------------------------------------------- delays

/// mob.cpp mobskill_delay: the delays of the monster skill in md->skill_idx, at cast end and cast cancel (the caller
/// saves `md`). Unless monster_ai 0x200, the delay of the first entry of the same skill that matches the monster's
/// current state goes to every entry of that skill (none when no entry matches any more).
pub fn mobskill_delay(c: &Ctx, md: &mut MobData, tick: Tick) {
    // If skill was used by a script, do not apply any skill delay
    if md.skill_idx < 0 {
        return;
    }
    let Some(db) = super::db(c, md) else { return };
    let (skill_idx, skillstate, target_id) = (md.skill_idx as usize, md.skillstate, md.target_id);
    mobskill_delay_apply(&db.skill, skill_idx, skillstate, target_id, tick, &mut md.skilldelay);
}

/// mobskill_delay's rule on a skill list (md->db->skill) and md->skilldelay.
pub fn mobskill_delay_apply(ms: &[MobSkill], skill_idx: usize, skillstate: i32, target_id: u32, tick: Tick, skilldelay: &mut Vec<Tick>) {
    if ms.is_empty() || skill_idx >= ms.len() {
        return;
    }
    // Officially the skill delay is per skill rather than per skill db entry. We simulate this by setting the delay
    // for all entries of the same skill. We have an optional mob AI setting to only set the delay for one entry
    if conf::mob_ai() & 0x200 == 0 {
        // Even though we already know which skill was picked as we stored that in skill_idx, officially it actually
        // tries to find the skill again using the monster's current state. If the skill cannot be found anymore
        // because the monster's state has changed no delay will be applied
        let skill_id = ms[skill_idx].skill_id;
        let mut delay = 0;
        for m in ms.iter() {
            if m.skill_id == skill_id {
                let matched = m.state == skillstate
                    || m.state == MSS_ANY
                    || (m.state == MSS_ANYTARGET && target_id != 0 && skillstate != MSS_LOOT);
                // State and skill match, use the first delay found
                if matched {
                    delay = m.delay;
                    break;
                }
            }
        }
        // Apply delay found to all entries of the skill
        for (i, m) in ms.iter().enumerate() {
            if m.skill_id == skill_id {
                skilldelay_set(skilldelay, i, tick + delay as Tick);
            }
        }
    } else {
        skilldelay_set(skilldelay, skill_idx, tick + ms[skill_idx].delay as Tick);
    }
}

// ---------------------------------------------------------------------------------------------- mobskill_use

/// mob.cpp mobskill_use: looks for a monster skill to use for `event` (-1: the AI's regular check, which also sets
/// last_skillcheck; -2: any damage; an MSC_* event; MSC_SKILLUSED | skill_id << 16) and casts the first entry whose
/// state, chance (the `rnd() % 10000` roll, made for every entry that passes the delay and state checks),
/// condition and target work out. monster_ai 0x100 starts at a random entry, 0x1000 goes on after a target / cast
/// failure (else the search stops there). Returns whether a skill was used; md->skill_idx is the entry used, -1 if
/// none.
pub fn mobskill_use(c: &Ctx, md_id: u32, tick: Tick, event: i32, damage: i64) -> bool {
    // Friend bl, which can either be a BL_PC or BL_MOB depending on the situation. [Skotlex] Like rAthena's locals,
    // fbl / fmd keep what a condition found for the entries after it.
    let mut fbl: Option<Unit> = None;
    let mut fmd: Option<Unit> = None;
    let Some(mut md) = get(c, md_id) else { return false };
    let Some(mut u) = unit::get(c, md_id) else { return false };
    // md->db (only a deleted clone has none)
    let Some(db) = super::db(c, &md) else { return false };
    let ms = &db.skill;
    if conf::mob_skill_rate() == 0 || u.ud.skilltimer != 0 || ms.is_empty() || super::has_mode(&md, MD_NOCAST) {
        return false;
    }
    // Monsters check their non-attack-state skills once per second, but we ignore this for events for now
    if event == -1 {
        md.last_skillcheck = tick;
        put(c, &md);
    }
    let len = ms.len();
    // Pick a starting position and loop from that.
    let mut i = if conf::mob_ai() & 0x100 != 0 { c.rnd() as usize % len } else { 0 };
    for n in 0..len {
        if n > 0 {
            i += 1;
        }
        if i == len {
            i = 0;
        }
        let e = &ms[i];
        if tick - skilldelay_get(&md, i) < 0 {
            continue;
        }
        let c2 = e.cond2;
        if e.state != md.skillstate {
            // ANYTARGET works with any state as long as there's a target. [Skotlex]
            let ok = md.skillstate != MSS_DEAD
                && (e.state == MSS_ANY || (e.state == MSS_ANYTARGET && md.target_id != 0 && md.skillstate != MSS_LOOT));
            if !ok {
                continue;
            }
        }
        if (c.rnd() % 10000) as i32 > e.permillage as i32 {
            // Lupus (max value = 10000)
            continue;
        }
        let flag = if e.cond1 as i32 == event {
            true // Trigger skill.
        } else if e.cond1 == MSC_SKILLUSED {
            (event & 0xffff) == MSC_SKILLUSED as i32 && ((event >> 16) as i64 == c2 || c2 == 0)
        } else if e.cond1 == MSC_GROUNDATTACKED && damage > 0 {
            (event & 0xffff) == MSC_SKILLUSED as i32 && skill_get_inf((event >> 16) as u16) & INF_GROUND_SKILL as i32 != 0
        } else if e.cond1 == MSC_DAMAGEDGT && damage > 0 && (event & 0xffff) != MSC_SKILLUSED as i32 {
            // Avoid double check if skill has been used [datawulf]
            damage > c2
        } else if event == -1 {
            // Avoid entering on defined events to avoid "hyper-active skill use" due to the overflow of calls to
            // this function in battle.
            match e.cond1 {
                MSC_ALWAYS => true,
                MSC_MYHPLTMAXRATE => {
                    // HP< maxhp%
                    let pct = get_percentage(md.status.hp, md.status.max_hp) as i32 as i64;
                    pct <= c2
                }
                MSC_MYHPINRATE => {
                    let pct = get_percentage(md.status.hp, md.status.max_hp) as i32;
                    pct as i64 >= c2 && pct <= e.val[0]
                }
                MSC_MYSTATUSON | MSC_MYSTATUSOFF => {
                    // status[num] on / off
                    let sc = crate::status::sc::load(c, md_id);
                    if sc.empty() {
                        false
                    } else {
                        mob_getstatus_sub(&sc, e.cond1, e.cond2 as i32)
                    }
                }
                MSC_FRIENDHPLTMAXRATE => {
                    // friend HP < maxhp%
                    fbl = mob_getfriendhprate(c, &md, &u, 0, e.cond2);
                    fbl.is_some()
                }
                MSC_FRIENDHPINRATE => {
                    fbl = mob_getfriendhprate(c, &md, &u, e.cond2, e.val[0] as i64);
                    fbl.is_some()
                }
                MSC_FRIENDSTATUSON | MSC_FRIENDSTATUSOFF => {
                    // friend status[num] on / off
                    fmd = mob_getfriendstatus(c, &md, &u, e.cond1 as i64, e.cond2);
                    fmd.is_some()
                }
                MSC_SLAVELT => (super::spawn::mob_countslave(c, &u) as i64) < c2, // slave < num
                MSC_ATTACKPCGT => unit_counttargeted(&u) as i64 > c2,             // attack pc > num
                MSC_SLAVELE => (super::spawn::mob_countslave(c, &u) as i64) <= c2, // slave <= num
                MSC_ATTACKPCGE => unit_counttargeted(&u) as i64 >= c2,            // attack pc >= num
                MSC_AFTERSKILL => u.ud.skill_id as i64 == c2,
                MSC_RUDEATTACKED => {
                    let f = md.attacked_count >= RUDE_ATTACKED_COUNT;
                    if f {
                        // Rude attacked count should be reset after the skill condition is met. Thanks to Komurka
                        // [Skotlex]
                        md.attacked_count = 0;
                        put(c, &md);
                    }
                    f
                }
                MSC_MASTERHPLTMAXRATE => {
                    fbl = mob_getmasterhpltmaxrate(c, &md, e.cond2);
                    fbl.is_some()
                }
                MSC_MASTERATTACKED => {
                    if md.master_id > 0 {
                        fbl = map_id2bl(c, md.master_id);
                        fbl.as_ref().map(|b| unit_counttargeted(b) > 0).unwrap_or(false)
                    } else {
                        false
                    }
                }
                MSC_ALCHEMIST => md.special_ai != AI_NONE && md.trickcasting == 0 && md.status.hp < md.status.max_hp,
                MSC_MOBNEARBYGT => (mob_count_nearby(c, &u, e.cond1) as i64) > c2,
                MSC_TRICKCASTING => md.trickcasting > 0,
                _ => false,
            }
        } else {
            false
        };
        if !flag {
            continue; // Skill requisite failed to be fulfilled.
        }
        // Execute skill
        let (skill_id, skill_lv) = (e.skill_id, e.skill_lv);
        // A friend / master found by a condition is taken as it is now (rAthena's pointers are live objects).
        let fresh = |b: Option<Unit>| b.map(|b| unit::get(c, b.id).unwrap_or(b));
        // MST_FRIEND (and MST_MASTER without a master): the friend found, else the friend monster, else the monster.
        let friend = |fbl: &Option<Unit>, fmd: &Option<Unit>, u: &Unit| Some(fbl.clone().or_else(|| fmd.clone()).unwrap_or_else(|| u.clone()));
        let used = if skill_get_casttype(skill_id) == CastType::Ground {
            // Ground skill.
            let bl = match e.target {
                // Pick a random enemy within skill range.
                MST_RANDOM => battle_getenemy(c, &u, super::default_enemy_type(&md), crate::skill::misc::skill_get_range2(c, &u, skill_id, skill_lv, true)),
                MST_TARGET | MST_AROUND5 | MST_AROUND6 | MST_AROUND7 | MST_AROUND8 => map_id2bl(c, md.target_id),
                MST_MASTER => {
                    let mut b = Some(u.clone());
                    if md.master_id != 0 {
                        b = map_id2bl(c, md.master_id);
                    }
                    if b.is_some() {
                        b
                    } else {
                        friend(&fresh(fbl.clone()), &fresh(fmd.clone()), &u)
                    }
                }
                MST_FRIEND => friend(&fresh(fbl.clone()), &fresh(fmd.clone()), &u),
                _ => Some(u.clone()),
            };
            let Some(bl) = bl else {
                if conf::mob_ai() & 0x1000 != 0 {
                    continue;
                }
                break;
            };
            let (mut x, mut y) = (bl.x, bl.y);
            // Look for an area to cast the spell around...
            if e.target >= MST_AROUND5 {
                let j = if e.target >= MST_AROUND1 { (e.target - MST_AROUND1) + 1 } else { (e.target - MST_AROUND5) + 1 };
                // map_search_freecell(md, md->m, &x, &y, j, j, 3): on failure x / y stay at the centre
                if let Some((nx, ny)) = super::spawn::map_search_freecell(c, Some((u.map, u.x, u.y)), u.map as i32, x, y, j, j, 3, 50) {
                    x = nx;
                    y = ny;
                }
            }
            md.skill_idx = i as i8;
            put(c, &md);
            let range = crate::skill::misc::skill_get_range2(c, &u, skill_id, skill_lv, true);
            battle_check_range(c, &u, &bl, range) && crate::skill::unit::unit_skilluse_pos2(c, md_id, x, y, skill_id, skill_lv, e.casttime, e.cancel as i32, false) != 0
        } else {
            // Targetted skill
            let bl = match e.target {
                // Pick a random enemy within skill range.
                MST_RANDOM => battle_getenemy(c, &u, super::default_enemy_type(&md), crate::skill::misc::skill_get_range2(c, &u, skill_id, skill_lv, true)),
                MST_TARGET => {
                    let b = map_id2bl(c, md.target_id);
                    // Monsters that cannot attack put their last attacker as target
                    if b.is_none() && !super::has_mode(&md, MD_CANATTACK) {
                        map_id2bl(c, md.attacked_id)
                    } else {
                        b
                    }
                }
                MST_MASTER => {
                    let mut b = Some(u.clone());
                    if md.master_id != 0 {
                        b = map_id2bl(c, md.master_id);
                    }
                    if b.is_some() {
                        b
                    } else {
                        friend(&fresh(fbl.clone()), &fresh(fmd.clone()), &u)
                    }
                }
                MST_FRIEND => friend(&fresh(fbl.clone()), &fresh(fmd.clone()), &u),
                _ => Some(u.clone()),
            };
            let Some(bl) = bl else {
                if conf::mob_ai() & 0x1000 != 0 {
                    continue;
                }
                break;
            };
            md.skill_idx = i as i8;
            put(c, &md);
            let range = crate::skill::misc::skill_get_range2(c, &u, skill_id, skill_lv, true);
            battle_check_range(c, &u, &bl, range) && crate::skill::unit::unit_skilluse_id2(c, md_id, bl.id, skill_id, skill_lv, e.casttime, e.cancel as i32, false) != 0
        };
        // The cast may have ended at once and changed the rows (skill_idx, delays, target, the effect itself).
        let last_md = md.clone();
        let last_u = u.clone();
        match (get(c, md_id), unit::get(c, md_id)) {
            (Some(m), Some(uu)) => {
                md = m;
                u = uu;
            }
            _ => {
                // The monster is gone (rAthena's FreeBlockLock keeps its memory until the end of the call).
                if used && e.msg_id != 0 {
                    chat_display(c, &last_md, &last_u, e.msg_id);
                }
                return used;
            }
        }
        if !used {
            if conf::mob_ai() & 0x1000 != 0 {
                continue;
            }
            break;
        }
        // Skill used. Post-setups...
        if e.msg_id != 0 {
            // Display color message [SnakeDrak]
            chat_display(c, &md, &u, e.msg_id);
        }
        return true;
    }
    // No skill was used.
    if let Some(mut m) = get(c, md_id) {
        m.skill_idx = -1;
        put(c, &m);
    }
    false
}

// ---------------------------------------------------------------------------------------------- mobskill_event

/// mob.cpp mobskill_event: `src` targeted the monster with a skill (flag -1: MSC_CASTTARGETED), used a skill on it
/// (MSC_SKILLUSED | skill_id << 16) or hit it (BF_SHORT: MSC_CLOSEDATTACKED; BF_LONG without BF_MAGIC:
/// MSC_LONGRANGEATTACKED; other damage: -2). The monster takes `src` as its target for the skill search (if it has
/// none, or with mob_changetarget_byskill) and keeps it only when a skill was used on an enemy. Returns whether a
/// skill was used.
pub fn mobskill_event(c: &Ctx, md_id: u32, src: &Unit, tick: Tick, flag: i32, damage: i64) -> i32 {
    let Some(mut md) = get(c, md_id) else { return 0 };
    let on_map = unit::get(c, md_id).map(|u| u.on_map).unwrap_or(false);
    if !on_map || md.status.hp == 0 {
        return 0;
    }
    let target_id = md.target_id;
    if target_id == 0 || conf::mob_changetarget_byskill() {
        md.target_id = src.id;
        put(c, &md);
    }
    let res = if flag == -1 {
        mobskill_use(c, md_id, tick, MSC_CASTTARGETED as i32, 0)
    } else if (flag & 0xffff) == MSC_SKILLUSED as i32 {
        mobskill_use(c, md_id, tick, flag, damage)
    } else if flag & BF_SHORT != 0 {
        mobskill_use(c, md_id, tick, MSC_CLOSEDATTACKED as i32, damage)
    } else if flag & BF_LONG != 0 && flag & BF_MAGIC == 0 {
        // Long-attacked should not include magic.
        mobskill_use(c, md_id, tick, MSC_LONGRANGEATTACKED as i32, damage)
    } else if damage > 0 {
        // Trigger for any damage dealt from other attack types without affecting other triggers [datawulf]
        mobskill_use(c, md_id, tick, -2, damage)
    } else {
        false
    };
    let restore = if !res {
        // Restore previous target only if skill condition failed to trigger. [Skotlex]
        true
    } else {
        // Otherwise check if the target is an enemy, and unlock if needed.
        match unit::get(c, md_id) {
            Some(mu) => {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                battle_check_target(c, &mu, &s, BCT_ENEMY) <= 0
            }
            None => false,
        }
    };
    if restore {
        if let Some(mut m) = get(c, md_id) {
            m.target_id = target_id;
            put(c, &m);
        }
    }
    res as i32
}

// ---------------------------------------------------------------------------------------------- cast end

/// skill.cpp skill_castend_id / skill_castend_pos: a monster's skill emotion at cast end
/// (`md->skill_idx >= 0 && md->db->skill[md->skill_idx]->emotion` in [ET_SURPRISE, ET_MAX)).
pub fn castend_emotion(c: &Ctx, md_id: u32) {
    let Some(md) = get(c, md_id) else { return };
    if md.skill_idx < 0 {
        return;
    }
    let Some(db) = super::db(c, &md) else { return };
    let Some(ms) = db.skill.get(md.skill_idx as usize) else { return };
    if ms.emotion >= ET_SURPRISE && ms.emotion < ET_MAX {
        if let Some(u) = unit::get(c, md_id) {
            crate::clif::emotion(c, &u, ms.emotion as u8);
        }
    }
}

// ---------------------------------------------------------------------------------------------- clones

/// mob.cpp mob_clone_disabled_skills (formerly skill_nocast flag 16).
pub fn mob_clone_disabled_skills(skill_id: u16) -> bool {
    matches!(skill_id, PR_TURNUNDEAD | PR_MAGNUS)
}

/// The s_mob_skill mob_clone_spawn makes for one skill of the player's tree, from the skill's `casttime`
/// (skill_castfix) and `delayfix` (skill_delayfix), and how many times it goes into the list (`len` entries are there
/// already): 2 for a support skill, because rAthena pushes the same object once more "so it also triggers on self"
/// — both entries end as the self version. None for a skill clones do not use.
pub fn mob_clone_skill_entry(skill_id: u16, skill_lv: u16, casttime: i32, delayfix: i32, len: usize) -> Option<(MobSkill, usize)> {
    // make_shared<s_mob_skill>() value-initialises: cond1 MSC_ALWAYS, cond2 0, target MST_TARGET, val 0
    let mut ms = MobSkill {
        skill_id,
        skill_lv,
        state: MSS_ANY,
        permillage: (500 * conf::mob_skill_rate() / 100) as i16, // Default chance of all skills: 5%
        emotion: -1,
        cancel: 0,
        casttime,
        delay: 5000 + delayfix,
        msg_id: 0,
        ..Default::default()
    };
    let mut pushes = 1;
    let inf = skill_get_inf(skill_id);
    if inf & INF_ATTACK_SKILL as i32 != 0 {
        ms.target = MST_TARGET;
        ms.cond1 = MSC_ALWAYS;
        ms.state = if skill_get_range(skill_id, ms.skill_lv) > 3 { MSS_ANYTARGET } else { MSS_BERSERK };
    } else if inf & INF_GROUND_SKILL as i32 != 0 {
        if skill_get_inf2(skill_id, &[INF2_ISTRAP]) {
            // Traps!
            ms.state = MSS_IDLE;
            ms.target = MST_AROUND2;
            ms.delay = 60000;
        } else if skill_get_unit_target(skill_id) == crate::battle::BCT_ENEMY {
            // Target Enemy
            ms.state = MSS_ANYTARGET;
            ms.target = MST_TARGET;
            ms.cond1 = MSC_ALWAYS;
        } else {
            // Target allies
            ms.target = MST_FRIEND;
            ms.cond1 = MSC_FRIENDHPLTMAXRATE;
            ms.cond2 = 95;
        }
    } else if inf & INF_SELF_SKILL as i32 != 0 {
        if skill_get_inf2(skill_id, &[INF2_NOTARGETSELF]) {
            // auto-select target skill.
            ms.target = MST_TARGET;
            ms.cond1 = MSC_ALWAYS;
            ms.state = if skill_get_range(skill_id, ms.skill_lv) > 3 { MSS_ANYTARGET } else { MSS_BERSERK };
        } else {
            // Self skill
            ms.target = MST_SELF;
            ms.cond1 = MSC_MYHPLTMAXRATE;
            ms.cond2 = 90;
            ms.permillage = 2000;
            // Delay: Remove the stock 5 secs and add half of the support time.
            ms.delay += -5000 + (skill_get_time(skill_id, ms.skill_lv) + skill_get_time2(skill_id, ms.skill_lv)) / 2;
            if ms.delay < 5000 {
                ms.delay = 5000; // With a minimum of 5 secs.
            }
        }
    } else if inf & INF_SUPPORT_SKILL as i32 != 0 {
        ms.target = MST_FRIEND;
        ms.cond1 = MSC_FRIENDHPLTMAXRATE;
        ms.cond2 = 90;
        if skill_id == AL_HEAL {
            ms.permillage = 5000; // Higher skill rate usage for heal.
        } else if skill_id == ALL_RESURRECTION {
            ms.cond2 = 1;
        }
        // Delay: Remove the stock 5 secs and add half of the support time.
        ms.delay += -5000 + (skill_get_time(skill_id, ms.skill_lv) + skill_get_time2(skill_id, ms.skill_lv)) / 2;
        if ms.delay < 2000 {
            ms.delay = 2000; // With a minimum of 2 secs.
        }
        if len < MAX_MOBSKILL {
            // duplicate this so it also triggers on self.
            ms.target = MST_SELF;
            ms.cond1 = MSC_MYHPLTMAXRATE;
            pushes = 2;
        }
    } else {
        match skill_id {
            // Certain Special skills that are passive, and thus, never triggered.
            MO_TRIPLEATTACK | TF_DOUBLE | GS_CHAINACTION => {
                ms.state = MSS_BERSERK;
                ms.target = MST_TARGET;
                ms.cond1 = MSC_ALWAYS;
                ms.permillage = if skill_id == MO_TRIPLEATTACK { 3000 - ms.skill_lv as i32 * 100 } else { ms.skill_lv as i32 * 500 } as i16;
                ms.delay -= 5000; // Remove the added delay as these could trigger on "all hits".
            }
            _ => return None, // Untreated Skill
        }
    }
    if conf::mob_skill_rate() != 100 {
        ms.permillage = (ms.permillage as i32 * conf::mob_skill_rate() / 100) as i16;
    }
    if conf::mob_skill_delay() != 100 {
        ms.delay = ms.delay * conf::mob_skill_delay() / 100;
    }
    Some((ms, pushes))
}

/// mob.cpp mob_clone_spawn's skill list: the skills of the player's tree, highest id first ("Go Backwards to give
/// better priority to advanced skills"), that the player has learned and could cast now
/// (skill_check_condition_castbegin), at most MAX_MOBSKILL entries (one more after a support skill, see
/// [`mob_clone_skill_entry`]). flag & 1 (a friendly clone) keeps the ground skills whose units never hit monsters or
/// players.
///
/// rAthena sets the player's fd to 0 while it builds the list ("so it doesn't receive the messages from
/// skill_check_condition_castbegin"): world::with_muted drops the player's packets meanwhile.
pub fn mob_clone_skills(c: &Ctx, sd: &Unit, flag: i32) -> Vec<MobSkill> {
    match crate::pc::identity_of(c, sd.id) {
        Some(who) => crate::world::with_muted(who, || mob_clone_skills_sub(c, sd, flag)),
        None => mob_clone_skills_sub(c, sd, flag),
    }
}

fn mob_clone_skills_sub(c: &Ctx, sd: &Unit, flag: i32) -> Vec<MobSkill> {
    let mut list: Vec<MobSkill> = Vec::new();
    let Some(ch) = crate::pc::character_of(c, sd.id) else { return list };
    let Some(tree) = crate::db::jobs::skill_tree_db().find(ch.class) else { return list };
    if tree.skills.is_empty() {
        return list;
    }
    let skills = crate::skilltree::Skills::load(c, sd.id);
    let lv_at = |idx: u16| skills.as_ref().map(|s| s.at(idx).lv).unwrap_or(0) as u16;
    let mut skill_list: Vec<u16> = tree.skills.iter().map(|e| e.skill_id).collect();
    skill_list.sort_by(|a, b| b.cmp(a)); // std::sort(skill_list.rbegin(), skill_list.rend())
    for skill_id in skill_list {
        if list.len() >= MAX_MOBSKILL {
            break;
        }
        if skill_id == 0 {
            continue;
        }
        let sk_idx = skill_get_index(skill_id);
        if sk_idx == 0 || lv_at(sk_idx) < 1 || skill_get_inf2(skill_id, &[INF2_ISWEDDING, INF2_ISGUILD]) || mob_clone_disabled_skills(skill_id) {
            continue;
        }
        // Normal aggressive mob, disable skills that cannot help them fight against players (those with flags
        // UF_NOMOB and UF_NOPC are specific to always aid players!) [Skotlex]
        if flag & 1 == 0 && skill_get_unit_id(skill_id) != 0 && skill_get_unit_flag(skill_id, &[UF_NOMOB, UF_NOPC]) {
            continue;
        }
        let skill_lv = lv_at(sk_idx);
        // The clone should be able to cast the skill (e.g. have the required weapon) bugreport:5299)
        if !crate::skill::condition::skill_check_condition_castbegin(c, sd, skill_id, skill_lv) {
            continue;
        }
        let casttime = crate::skill::castfix::skill_castfix(c, sd, skill_id, skill_lv);
        let delayfix = crate::skill::castfix::skill_delayfix(c, sd, skill_id, skill_lv);
        let Some((ms, pushes)) = mob_clone_skill_entry(skill_id, skill_lv, casttime, delayfix, list.len()) else { continue };
        for _ in 0..pushes {
            list.push(ms.clone());
        }
    }
    list
}

#[cfg(test)]
mod tests {
    use super::*;

    fn k(n: &str) -> i64 {
        crate::data::constant(n).unwrap_or_else(|| panic!("constant {n}"))
    }

    /// The emotion bounds equal clif.hpp's emotion_type (script constants).
    #[test]
    fn emotion_constants() {
        assert_eq!(ET_SURPRISE as i64, k("ET_SURPRISE"));
        assert_eq!(ET_MAX as i64, k("ET_YUT7") + 1); // ET_MAX follows ET_YUT7
    }

    /// mob.hpp e_mob_skill_condition / e_mob_skill_target / MobSkillState (not script constants: the header's order).
    #[test]
    fn mob_skill_enums() {
        assert_eq!((MSC_ALWAYS, MSC_SKILLUSED, MSC_CASTTARGETED, MSC_RUDEATTACKED, MSC_SPAWN, MSC_TRICKCASTING), (0, 16, 17, 18, 22, 26));
        assert_eq!((MST_TARGET, MST_FRIEND, MST_AROUND5, MST_AROUND8, MST_AROUND1, MST_AROUND4, MST_AROUND), (0, 3, 5, 8, 9, 12, 12));
        assert_eq!((MSS_ANY, MSS_IDLE, MSS_DEAD, MSS_BERSERK, MSS_ANYTARGET), (-1, 0, 3, 4, 8));
        assert_eq!(MAX_MOBSKILL, 50);
    }

    /// get_percentage's double arithmetic: 100 * (29 / 100.0) is 28.999999999999996.
    #[test]
    fn percentage() {
        assert_eq!(get_percentage(29, 100), 28);
        assert_eq!(get_percentage(50, 100), 50);
        assert_eq!(get_percentage(1, 3), 33);
        assert_eq!(get_percentage(55, 55), 100);
        assert_eq!(get_percentage(0, 55), 0);
        assert_eq!(get_percentage(1, 0), u32::MAX);
    }

    fn sce(t: i32) -> crate::status::data::ScEntry {
        crate::status::data::ScEntry { id: 1, bl_id: 1, sc_type: t, timer: 0, val1: 0, val2: 0, val3: 0, val4: 0 }
    }

    #[test]
    fn getstatus_sub() {
        use crate::status::consts::{SC_PROVOKE, SC_STONE, SC_STUN};
        let none = Sc::default();
        let stun = Sc { list: vec![sce(SC_STUN)] };
        let provoke = Sc { list: vec![sce(SC_PROVOKE)] };
        // anybad (SC_NONE): any of the common ailments
        assert!(mob_getstatus_sub(&stun, MSC_MYSTATUSON, SC_NONE));
        assert!(!mob_getstatus_sub(&provoke, MSC_MYSTATUSON, SC_NONE));
        assert!(mob_getstatus_sub(&provoke, MSC_FRIENDSTATUSOFF, SC_NONE));
        assert!(mob_getstatus_sub(&provoke, MSC_MYSTATUSON, SC_PROVOKE));
        assert!(!mob_getstatus_sub(&none, MSC_MYSTATUSON, SC_STONE));
        assert!(mob_getstatus_sub(&none, MSC_MYSTATUSOFF, SC_STONE));
        // other conditions are never met
        assert!(!mob_getstatus_sub(&stun, MSC_ALWAYS, SC_NONE));
    }

    fn entry(skill_id: u16, state: i32, delay: i32) -> MobSkill {
        MobSkill { skill_id, state, delay, ..Default::default() }
    }

    #[test]
    fn delay_rule() {
        // entries 0 and 2 are the same skill; the monster (berserk) matches entry 2's state only
        let ms = vec![entry(184, MSS_IDLE, 1000), entry(197, MSS_ANY, 3000), entry(184, MSS_BERSERK, 5000)];
        let mut d = vec![0; MAX_MOBSKILL];
        mobskill_delay_apply(&ms, 0, MSS_BERSERK, 0, 100, &mut d);
        assert_eq!((d[0], d[1], d[2]), (5100, 0, 5100));
        // no entry of the skill matches the state any more: delay 0 for every entry
        let mut d = vec![0; MAX_MOBSKILL];
        mobskill_delay_apply(&ms, 0, MSS_WALK, 0, 100, &mut d);
        assert_eq!((d[0], d[2]), (100, 100));
        // MSS_ANYTARGET needs a target and no loot state
        let ms = vec![entry(10, MSS_ANYTARGET, 700)];
        let mut d = vec![0; MAX_MOBSKILL];
        mobskill_delay_apply(&ms, 0, MSS_RUSH, 5, 100, &mut d);
        assert_eq!(d[0], 800);
        mobskill_delay_apply(&ms, 0, MSS_LOOT, 5, 1000, &mut d);
        assert_eq!(d[0], 1000);
        // an index past the list does nothing; entry MAX_MOBSKILL of a clone grows the array
        mobskill_delay_apply(&ms, 3, MSS_RUSH, 5, 2000, &mut d);
        assert_eq!(d[0], 1000);
        let big: Vec<MobSkill> = (0..=MAX_MOBSKILL as u16).map(|i| entry(1000 + i, MSS_ANY, 10)).collect();
        let mut d = vec![0; MAX_MOBSKILL];
        mobskill_delay_apply(&big, MAX_MOBSKILL, MSS_IDLE, 0, 5, &mut d);
        assert_eq!((d.len(), d[MAX_MOBSKILL]), (MAX_MOBSKILL + 1, 15));
    }

    #[test]
    fn clone_entries() {
        // AL_HEAL (support): pushed twice, both as the self version, 50% chance, delay at least 2 s
        let (ms, n) = mob_clone_skill_entry(AL_HEAL, 10, 0, 0, 0).expect("heal");
        assert_eq!((n, ms.target, ms.cond1, ms.cond2, ms.permillage, ms.state), (2, MST_SELF, MSC_MYHPLTMAXRATE, 90, 5000, MSS_ANY));
        assert_eq!((ms.delay, ms.emotion, ms.cancel, ms.msg_id), (2000, -1, 0, 0));
        // with the list full but for one entry the support skill stays the friend version, pushed once
        let (ms, n) = mob_clone_skill_entry(AL_HEAL, 10, 0, 0, MAX_MOBSKILL).expect("heal");
        assert_eq!((n, ms.target, ms.cond1), (1, MST_FRIEND, MSC_FRIENDHPLTMAXRATE));
        // ALL_RESURRECTION: cond2 1
        let (ms, _) = mob_clone_skill_entry(ALL_RESURRECTION, 4, 0, 0, 0).expect("resurrection");
        assert_eq!((ms.cond2, ms.permillage), (1, 500));
        // MG_FIREBOLT (attack skill, range 9): any-target state; the cast time and delay come from the caller
        let (ms, n) = mob_clone_skill_entry(MG_FIREBOLT, 10, 4000, 1200, 0).expect("fire bolt");
        assert_eq!((n, ms.target, ms.cond1, ms.state, ms.casttime, ms.delay, ms.permillage), (1, MST_TARGET, MSC_ALWAYS, MSS_ANYTARGET, 4000, 6200, 500));
        // SM_BASH (attack skill, melee): berserk state
        let (ms, _) = mob_clone_skill_entry(SM_BASH, 10, 0, 0, 0).expect("bash");
        assert_eq!(ms.state, MSS_BERSERK);
        // TF_DOUBLE (passive): berserk, 500 per level, no stock delay
        let (ms, _) = mob_clone_skill_entry(TF_DOUBLE, 10, 0, 0, 0).expect("double attack");
        assert_eq!((ms.state, ms.permillage, ms.delay), (MSS_BERSERK, 5000, 0));
        // MO_TRIPLEATTACK: 3000 - 100 per level
        let (ms, _) = mob_clone_skill_entry(MO_TRIPLEATTACK, 10, 0, 0, 0).expect("triple attack");
        assert_eq!(ms.permillage, 2000);
        // HT_LANDMINE (a trap): idle, around 2 cells, 60 s
        let (ms, _) = mob_clone_skill_entry(HT_LANDMINE, 5, 0, 0, 0).expect("land mine");
        assert_eq!((ms.state, ms.target, ms.delay), (MSS_IDLE, MST_AROUND2, 60000));
        // SM_SWORD (passive, not special): not used
        assert!(mob_clone_skill_entry(SM_SWORD, 10, 0, 0, 0).is_none());
        assert!(mob_clone_disabled_skills(PR_MAGNUS) && mob_clone_disabled_skills(PR_TURNUNDEAD) && !mob_clone_disabled_skills(AL_HEAL));
    }
}

// mob.cpp mob_random_class, ported with NPC_METAMORPHOSIS / NPC_TRANSFORMATION (skills/npc/metamorphosis.rs).
#[allow(unused_imports)]
pub(crate) use crate::skills::npc::metamorphosis::mob_random_class;
