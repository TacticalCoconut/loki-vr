//! The pet's effects on its owner (pet.cpp): pet_skill_bonus_timer (the petskillbonus bonus, pet_skill_bonus: on for
//! `duration` s, off for `delay` s), pet_recovery_timer (petrecovery: cures one status change), pet_heal_timer and
//! pet_skill_support_timer (petskillsupport; the skill use itself is the skill families'), the pet autobonuses
//! (pet_addautobonus / pet_delautobonus / pet_exeautobonus / pet_endautobonus, script_run_petautobonus), and the
//! bonus / support scripts of the pet database run on the owner (status_calc_pc's "Pet Bonus", pet_data_init).

use std::cell::Cell;

use super::data::{self, PetAutobonus};
use super::get_percentage;
use crate::script::code::{register_provider, ExtSource};
use crate::timer::{self, Func};
use crate::unit;
use crate::world::{Ctx, Tick};

// ---------------------------------------------------------------------------------------------- scripts

thread_local! {
    static REGISTERED: Cell<bool> = const { Cell::new(false) };
}

/// The script sources of the pet system (script::code providers):
/// - `pet_bonus` / `pet_support`, key = the pet's monster id: s_pet_db::pet_bonus_script / pet_support_script
///   (PetDatabase::parseBodyNode: parse_script(…, SCRIPT_IGNORE_EXTERNAL_BRACKETS));
/// - `petautobonus`, key = the script text: script_add_petautobonus's parse_script(autobonus, "petautobonus", 0, 0).
pub fn ensure_providers() {
    if REGISTERED.with(|r| r.get()) {
        return;
    }
    register_provider("pet_bonus", pet_bonus_provider);
    register_provider("pet_support", pet_support_provider);
    register_provider("petautobonus", petautobonus_provider);
    REGISTERED.with(|r| r.set(true));
}

fn db_script(key: &str, bonus: bool) -> Option<ExtSource> {
    let class: u16 = key.parse().ok()?;
    let p = crate::db::misc::pet::pet_db().find(class)?;
    let text = if bonus { p.pet_bonus_script.as_deref()? } else { p.pet_support_script.as_deref()? };
    Some(ExtSource {
        text: text.as_bytes().to_vec(),
        file: "db/re/pet_db.yml".to_string(),
        line: 0,
        options: crate::script::compiler::SCRIPT_IGNORE_EXTERNAL_BRACKETS,
        userfuncs: Vec::new(),
    })
}

fn pet_bonus_provider(_c: Option<&Ctx>, key: &str) -> Option<ExtSource> {
    db_script(key, true)
}

fn pet_support_provider(_c: Option<&Ctx>, key: &str) -> Option<ExtSource> {
    db_script(key, false)
}

fn petautobonus_provider(_c: Option<&Ctx>, key: &str) -> Option<ExtSource> {
    Some(ExtSource { text: crate::net::wire::latin1(key), file: "petautobonus".to_string(), line: 0, options: 0, userfuncs: Vec::new() })
}

/// status.cpp status_calc_pc_sub, "Pet Bonus": the pet database's bonus script runs on the owner, then the
/// petskillbonus bonus while it is on (intimacy > 0, the accessory worn with pet_equip_required, state.skillbonus).
/// Called by status/calc.rs at that place with the owner's working status row.
pub fn status_calc_pc_pet_bonus(c: &Ctx, sd: &mut crate::status::data::PcStatus, run: &dyn Fn(&Ctx, &mut crate::status::data::PcStatus, &str, &str)) {
    let Some(pd) = data::of_master(c, sd.account_id) else { return };
    let Some(pdb) = data::pet_db_of(&pd) else { return };
    if pdb.pet_bonus_script.is_some() {
        ensure_providers();
        run(c, sd, "pet_bonus", &pd.pet.class_.to_string());
    }
    // (the script may have changed the pet? it cannot: getpetinfo only reads)
    let Some(pd) = data::of_master(c, sd.account_id) else { return };
    if pd.pet.intimate > 0 && (!super::pet_equip_required() || pd.pet.equip > 0) && pd.skillbonus {
        if let Some(b) = &pd.bonus {
            crate::status::bonus::pc_bonus(c, sd, b.bonus_type as i32, b.val as i32);
        }
    }
}

/// pet_data_init / pet_reload_sub with pet_status_support: `run_script(pet_db_ptr->pet_support_script, 0, sd->id, 0)`
/// (the support script calls petskillbonus, petloot, petrecovery, petskillsupport … on the new pet).
pub fn run_support_script(c: &Ctx, aid: u32, pdb: &crate::db::misc::pet::PetDb) {
    if pdb.pet_support_script.is_some() {
        ensure_providers();
        crate::script::run_ext(c, "pet_support", &pdb.class_.to_string(), aid, 0);
    }
}

/// script.cpp script_run_petautobonus: the compiled autobonus script (pet_autobonuses) runs on the owner.
fn script_run_petautobonus(c: &Ctx, autobonus: &str, aid: u32) {
    ensure_providers();
    crate::script::run_ext(c, "petautobonus", autobonus, aid, 0);
}

// ---------------------------------------------------------------------------------------------- timers

/// pet.cpp pet_skill_bonus_timer (target = the owner's account id): the petskillbonus bonus goes on for `duration` s
/// and off for `delay` s (with no delay it stays on); the owner's status follows. A pet that lost its intimacy stops.
pub fn pet_skill_bonus_timer(c: &Ctx, tid: u64, tick: Tick, id: u32) -> i32 {
    if crate::pc::get(c, id).is_none() {
        return 1;
    }
    let Some(mut pd) = data::of_master(c, id) else { return 1 };
    let Some(mut b) = pd.bonus.clone() else { return 1 };
    if b.timer != tid {
        log::error!("pet_skill_bonus_timer {} != {}", b.timer, tid);
        b.timer = 0;
        pd.bonus = Some(b);
        data::put(c, &pd);
        return 0;
    }
    // determine the time for the next timer
    let (bonus, timer_ms): (bool, i64) = if pd.skillbonus && b.delay > 0 {
        (false, b.delay as i64 * 1000) // the duration until pet bonuses will be reactivated again
    } else if pd.pet.intimate != 0 {
        (true, b.duration as i64 * 1000) // the duration for pet bonuses to be in effect
    } else {
        //Lost pet...
        b.timer = 0;
        pd.bonus = Some(b);
        data::put(c, &pd);
        return 0;
    };
    if pd.skillbonus != bonus {
        pd.skillbonus = bonus;
        data::put(c, &pd);
        crate::status::calc_pc(c, id, crate::status::defs::SCO_NONE);
    }
    // wait for the next timer
    let Some(mut pd) = data::of_master(c, id) else { return 0 };
    if let Some(b) = pd.bonus.as_mut() {
        b.timer = timer::add(c, tick + timer_ms, Func::PetSkillBonusTimer, id, 0);
    }
    data::put(c, &pd);
    0
}

/// pet.cpp pet_recovery_timer (target = the owner's account id): if the owner still has the status change the pet
/// cures, it ends (with TF_DETOXIFY's effect packet and the pet's ET_OK emotion).
pub fn pet_recovery_timer(c: &Ctx, tid: u64, id: u32) -> i32 {
    if crate::pc::get(c, id).is_none() {
        return 1;
    }
    let Some(mut pd) = data::of_master(c, id) else { return 1 };
    let Some(mut rec) = pd.recovery.clone() else { return 1 };
    if rec.timer != tid {
        log::error!("pet_recovery_timer {} != {}", rec.timer, tid);
        return 0;
    }
    if crate::status::sc::has_sce(c, id, rec.sc_type) {
        //Display a heal animation? Detoxify is chosen for now.
        if let (Some(pu), Some(sdu)) = (unit::get(c, pd.id), unit::get(c, id)) {
            crate::battle::atcommand::clif_skill_nodamage(c, Some(&pu), &sdu, crate::db::skills::ids::TF_DETOXIFY, 1, true);
        }
        crate::status::sc::status_change_end(c, id, rec.sc_type, 0);
        if let Some(pu) = unit::get(c, pd.id) {
            crate::clif::emotion(c, &pu, super::ET_OK);
        }
        pd = data::of_master(c, id).unwrap_or(pd);
    }
    rec.timer = 0;
    if let Some(r) = pd.recovery.as_mut() {
        r.timer = rec.timer;
    }
    data::put(c, &pd);
    0
}

/// The owner's HP / SP percentages, dead state and the pet's casting, in pet_heal_timer / pet_skill_support_timer's
/// order: `pc_isdead(sd) || (rate = get_percentage(sp)) > s_skill->sp || (rate = get_percentage(hp)) > s_skill->hp ||
/// (rate = (pd->ud.skilltimer != INVALID_TIMER))` — Some(rate) means wait (rate stays at its last assignment).
fn support_wait(c: &Ctx, id: u32, pu: &unit::Unit, s: &data::PetSkillSupport, init: u32) -> Option<u32> {
    let st = crate::status::data::sd_get(c, id).map(|sd| sd.battle_status).unwrap_or_default();
    let mut rate = init;
    if crate::pc::isdead(c, id) {
        return Some(rate);
    }
    rate = get_percentage(st.sp, st.max_sp);
    if rate > s.sp as u32 {
        return Some(rate);
    }
    rate = get_percentage(st.hp, st.max_hp);
    if rate > s.hp as u32 {
        return Some(rate);
    }
    rate = (pu.ud.skilltimer != 0) as u32;
    if rate != 0 {
        return Some(rate);
    }
    None
}

/// pet.cpp pet_heal_timer (target = the owner's account id; a support skill of id 0): the pet heals its owner when his
/// SP and HP are at or below the skill's thresholds (else it waits 1 s for every 10% above).
pub fn pet_heal_timer(c: &Ctx, tid: u64, tick: Tick, id: u32) -> i32 {
    if crate::pc::get(c, id).is_none() {
        return 1;
    }
    let Some(mut pd) = data::of_master(c, id) else { return 1 };
    let Some(s) = pd.s_skill.clone() else { return 1 };
    if s.timer != tid {
        log::error!("pet_heal_timer {} != {}", s.timer, tid);
        return 0;
    }
    let Some(pu) = unit::get(c, pd.id) else { return 0 };
    // uint32 rate = 100
    if let Some(rate) = support_wait(c, id, &pu, &s, 100) {
        //Wait (how long? 1 sec for every 10% of remaining); uint32 arithmetic
        let w = (if rate > 10 { rate } else { 10 }).wrapping_mul(100) as Tick;
        if let Some(ss) = pd.s_skill.as_mut() {
            ss.timer = timer::add(c, c.tick() + w, Func::PetHealTimer, id, 0);
        }
        data::put(c, &pd);
        return 0;
    }
    if let Some(mut u) = unit::get(c, pd.id) {
        unit::stop_attack(c, &mut u);
        unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
        unit::save(c, &u);
    }
    if let (Some(pu), Some(sdu)) = (unit::get(c, pd.id), unit::get(c, id)) {
        crate::battle::atcommand::clif_skill_nodamage(c, Some(&pu), &sdu, crate::db::skills::ids::AL_HEAL, s.lv as i32, true);
    }
    crate::status::hp::status_heal(c, id, s.lv as i64, 0, 0, 0);
    let Some(mut pd) = data::of_master(c, id) else { return 0 };
    if let Some(ss) = pd.s_skill.as_mut() {
        ss.timer = timer::add(c, tick + s.delay as Tick * 1000, Func::PetHealTimer, id, 0);
    }
    data::put(c, &pd);
    0
}

/// pet.cpp pet_skill_support_timer (target = the owner's account id): when the pet can act and its owner's SP / HP
/// are at or below the thresholds, the pet stops and re-arms for `delay` s, then uses its support skill on him (the
/// skill pipeline's unit_skilluse_pos / unit_skilluse_id).
pub fn pet_skill_support_timer(c: &Ctx, tid: u64, tick: Tick, id: u32) -> i32 {
    if crate::pc::get(c, id).is_none() {
        return 1;
    }
    let Some(mut pd) = data::of_master(c, id) else { return 1 };
    let Some(s) = pd.s_skill.clone() else { return 1 };
    if s.timer != tid {
        log::error!("pet_skill_support_timer {} != {}", s.timer, tid);
        return 0;
    }
    let Some(pu) = unit::get(c, pd.id) else { return 0 };
    if pu.ud.canact_tick - tick > 0 {
        //Wait until the pet can act again.
        if let Some(ss) = pd.s_skill.as_mut() {
            ss.timer = timer::add(c, pu.ud.canact_tick, Func::PetSkillSupportTimer, id, 0);
        }
        data::put(c, &pd);
        return 0;
    }
    // int16 rate = 100: get_percentage's uint32 is truncated to int16
    if let Some(rate) = support_wait(c, id, &pu, &s, 100) {
        let rate = rate as i16 as i32;
        let w = if rate > 10 { rate } else { 10 } as Tick;
        if let Some(ss) = pd.s_skill.as_mut() {
            ss.timer = timer::add(c, tick + w * 100, Func::PetSkillSupportTimer, id, 0);
        }
        data::put(c, &pd);
        return 0;
    }
    if let Some(mut u) = unit::get(c, pd.id) {
        unit::stop_attack(c, &mut u);
        unit::stop_walking(c, &mut u, unit::USW_FIXPOS, 0);
        unit::save(c, &u);
    }
    let Some(mut pd) = data::of_master(c, id) else { return 0 };
    if let Some(ss) = pd.s_skill.as_mut() {
        ss.timer = timer::add(c, tick + s.delay as Tick * 1000, Func::PetSkillSupportTimer, id, 0);
    }
    data::put(c, &pd);
    // the skill pipeline (skillcore)
    if crate::db::skills::skill_get_inf(s.id) & crate::db::skills::INF_GROUND_SKILL as i32 != 0 {
        let (sx, sy) = unit::get(c, id).map(|u| (u.x, u.y)).unwrap_or((0, 0));
        crate::skill::unit::unit_skilluse_pos(c, pd.id, sx, sy, s.id, s.lv);
    } else {
        crate::skill::unit::unit_skilluse_id(c, pd.id, id, s.id, s.lv);
    }
    0
}

// ---------------------------------------------------------------------------------------------- autobonus

/// The pet's autobonus list `which` (1 autobonus, 2 autobonus2, 3 autobonus3).
fn list(pd: &mut data::PetData, which: u8) -> &mut Vec<PetAutobonus> {
    match which {
        2 => &mut pd.autobonus2,
        3 => &mut pd.autobonus3,
        _ => &mut pd.autobonus,
    }
}

/// pc.hpp MAX_PC_BONUS; the BF_* flags of battle.hpp.
const MAX_PC_BONUS: usize = 50;
const BF_WEAPON: u16 = 0x0001;
const BF_MAGIC: u16 = 0x0002;
const BF_MISC: u16 = 0x0004;
const BF_SHORT: u16 = 0x0010;
const BF_LONG: u16 = 0x0040;
const BF_SKILL: u16 = 0x0100;
const BF_NORMAL: u16 = 0x0200;
const BF_WEAPONMASK: u16 = BF_WEAPON | BF_MAGIC | BF_MISC;
const BF_RANGEMASK: u16 = BF_SHORT | BF_LONG;
const BF_SKILLMASK: u16 = BF_SKILL | BF_NORMAL;

/// pet.cpp pet_addautobonus: a new autobonus on the owner's pet (a script already there is not added twice); without
/// a range / attack type the flag gets both ranges / weapon. False when not added.
#[allow(clippy::too_many_arguments)]
pub fn pet_addautobonus(c: &Ctx, aid: u32, which: u8, script: &str, rate: i16, dur: u32, flag: u16, other_script: &str, onskill: bool) -> bool {
    let Some(mut pd) = data::of_master(c, aid) else { return false };
    let bonus = list(&mut pd, which);
    // Check if the same bonus already exists; Compare based on bonus script
    if bonus.iter().any(|b| b.bonus_script == script) {
        return false;
    }
    if bonus.len() == MAX_PC_BONUS {
        log::warn!("pet_addautobonus: Reached max ({MAX_PC_BONUS}) number of petautobonus per pet!");
        return false;
    }
    let mut flag = flag;
    if !onskill {
        if flag & BF_RANGEMASK == 0 {
            flag |= BF_SHORT | BF_LONG; //No range defined? Use both.
        }
        if flag & BF_WEAPONMASK == 0 {
            flag |= BF_WEAPON; //No attack type defined? Use weapon.
        }
        if flag & BF_SKILLMASK == 0 {
            if flag & (BF_MAGIC | BF_MISC) != 0 {
                flag |= BF_SKILL; //These two would never trigger without BF_SKILL
            }
            if flag & BF_WEAPON != 0 {
                flag |= BF_NORMAL | BF_SKILL;
            }
        }
    }
    if !(-10000..=10000).contains(&rate) {
        log::warn!("pet_addautobonus: Bonus rate {rate} exceeds -10000~10000 range, capping.");
    }
    bonus.push(PetAutobonus { rate: rate.clamp(-10000, 10000), atk_type: flag, bonus_script: script.to_string(), other_script: other_script.to_string(), duration: dur as i64, timer: 0 });
    data::put(c, &pd);
    true
}

/// pet.cpp pet_delautobonus: `restore` re-runs the bonus scripts of the active autobonuses (status_calc_pc) and keeps
/// the list; otherwise the list is emptied (~s_petautobonus deletes an active timer).
pub fn pet_delautobonus(c: &Ctx, aid: u32, which: u8, restore: bool) {
    let Some(mut pd) = data::of_master(c, aid) else { return };
    let entries = list(&mut pd, which).clone();
    for b in &entries {
        if b.timer != 0 && !b.bonus_script.is_empty() && restore {
            script_run_petautobonus(c, &b.bonus_script, aid);
        }
    }
    if restore {
        return;
    }
    let Some(mut pd) = data::of_master(c, aid) else { return };
    for b in list(&mut pd, which).drain(..) {
        if b.timer != 0 {
            timer::delete(c, b.timer);
        }
    }
    data::put(c, &pd);
}

/// pet.cpp pet_exeautobonus: autobonus `index` of list `which` triggers: its other script runs, the bonus stays for its
/// duration (pet_endautobonus), and the owner's status is recalculated (SCO_FORCE). The triggers are skill.cpp's
/// skill_additional_effect / skill_counter_additional_effect (battle and skill agents).
pub fn pet_exeautobonus(c: &Ctx, aid: u32, which: u8, index: usize) {
    let Some(mut pd) = data::of_master(c, aid) else { return };
    let Some(b) = list(&mut pd, which).get(index).cloned() else { return };
    if b.timer != 0 {
        timer::delete(c, b.timer);
    }
    if !b.other_script.is_empty() {
        script_run_petautobonus(c, &b.other_script, aid);
    }
    let Some(mut pd) = data::of_master(c, aid) else { return };
    if let Some(e) = list(&mut pd, which).get_mut(index) {
        e.timer = timer::add(c, c.tick() + b.duration, Func::PetEndAutobonus, aid, which as i64);
    }
    data::put(c, &pd);
    crate::status::calc_pc(c, aid, crate::status::defs::SCO_FORCE);
}

/// skill.cpp skill_counter_additional_effect's pet loops (skill.cpp:1883-1896 attacking: list 1; 1918-1931 attacked:
/// list 2): each autobonus of the owner's pet rolls rnd_value(0, 1000) against its rate, then its trigger flags must
/// cover `attack_type` (weapon, range and skill masks), then pet_exeautobonus. Called by battle/addeff.rs after the
/// player's own loop; skill_additional_effect's autobonus3 loop (skill.cpp:1672-1682: `atk_type` is the skill id) is
/// [`run_pet_autobonus3`].
pub fn run_pet_autobonus(c: &Ctx, aid: u32, which: u8, attack_type: i32) {
    let mut i = 0;
    loop {
        let Some(mut pd) = data::of_master(c, aid) else { return };
        let Some(it) = list(&mut pd, which).get(i).cloned() else { return };
        i += 1;
        if c.rnd_value(0, 1000) >= it.rate as i32 {
            continue;
        }
        let t = it.atk_type as i32 & attack_type;
        if !(t & BF_WEAPONMASK as i32 != 0 && t & BF_RANGEMASK as i32 != 0 && t & BF_SKILLMASK as i32 != 0) {
            continue; // one or more trigger conditions were not fulfilled
        }
        pet_exeautobonus(c, aid, which, i - 1);
    }
}

/// skill.cpp skill_additional_effect's pet autobonus3 loop: the pet's autobonus3 entries whose skill is `skill_id`.
pub fn run_pet_autobonus3(c: &Ctx, aid: u32, skill_id: u16) {
    let mut i = 0;
    loop {
        let Some(mut pd) = data::of_master(c, aid) else { return };
        let Some(it) = list(&mut pd, 3).get(i).cloned() else { return };
        i += 1;
        if c.rnd_value(0, 1000) >= it.rate as i32 {
            continue;
        }
        if it.atk_type != skill_id {
            continue;
        }
        pet_exeautobonus(c, aid, 3, i - 1);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::script::vm::{CodeKey, ExtCode};

    fn compiles(kind: &str, key: &str) -> bool {
        crate::script::code::get(None, &CodeKey::Ext(ExtCode { kind: kind.into(), key: key.into() })).is_some()
    }

    /// Every pet database Script / SupportScript compiles (PetDatabase::parseBodyNode's parse_script with
    /// SCRIPT_IGNORE_EXTERNAL_BRACKETS), and so does every petautobonus script text they give (script_add_petautobonus:
    /// parse_script(…, "petautobonus", 0, 0)).
    #[test]
    fn pet_scripts_compile() {
        ensure_providers();
        let mut bad = Vec::new();
        let mut n = 0;
        let mut auto = 0;
        for p in crate::db::misc::pet::pet_db().iter() {
            if let Some(t) = &p.pet_bonus_script {
                n += 1;
                if !compiles("pet_bonus", &p.class_.to_string()) {
                    bad.push(format!("pet_bonus {}", p.class_));
                }
                // petautobonus "<script>",…: the first string argument
                for part in t.split("petautobonus").skip(1) {
                    let Some(start) = part.find('"') else { continue };
                    let Some(len) = part[start + 1..].find('"') else { continue };
                    let text = &part[start + 1..start + 1 + len];
                    auto += 1;
                    if !compiles("petautobonus", text) {
                        bad.push(format!("petautobonus of {}: {text}", p.class_));
                    }
                }
            }
            if p.pet_support_script.is_some() {
                n += 1;
                if !compiles("pet_support", &p.class_.to_string()) {
                    bad.push(format!("pet_support {}", p.class_));
                }
            }
        }
        // 95 bonus scripts and 9 petautobonus texts in db/re/pet_db.yml
        assert!(n >= 90 && auto > 0, "{n} pet scripts, {auto} petautobonus scripts");
        assert!(bad.is_empty(), "{} of {n} pet scripts / {auto} petautobonus scripts failed: {:?}", bad.len(), &bad[..bad.len().min(20)]);
    }
}

/// pet.cpp pet_endautobonus (timer, target = the owner's account id, data = the list): the autobonus is over.
pub fn pet_endautobonus(c: &Ctx, tid: u64, id: u32, which: i64) -> i32 {
    if crate::pc::get(c, id).is_none() {
        return 0;
    }
    if let Some(mut pd) = data::of_master(c, id) {
        if let Some(b) = list(&mut pd, which as u8).iter_mut().find(|b| b.timer == tid) {
            b.timer = 0;
        }
        data::put(c, &pd);
    }
    crate::status::calc_pc(c, id, crate::status::defs::SCO_FORCE);
    0
}
