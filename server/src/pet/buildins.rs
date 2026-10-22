//! The pet script commands of script.cpp: pet / catchpet, bpet / birthpet, makepet, getpetinfo, petskillbonus,
//! petloot, petrecovery, petskillattack, petskillattack2, petskillsupport, petautobonus / petautobonus2 /
//! petautobonus3. The skill commands store the pet's skill data as rAthena does; using those skills is the skill
//! families' (pet_attackskill / pet_skill_support_timer).

use super::data::{self, PetBonus, PetLoot, PetRecovery, PetSkillAttack, PetSkillSupport, MAX_PETLOOT_SIZE, PET_CATCH_MAX, PET_CATCH_NORMAL};
use crate::script::buildins::{charid2sd, lat, BuildinFn};
use crate::script::vm::*;
use crate::timer::{self, Func};
use crate::world::Tick;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "catchpet" => catchpet,
        "birthpet" => birthpet,
        "makepet" => makepet,
        "getpetinfo" => getpetinfo,
        "petskillbonus" => petskillbonus,
        "petloot" => petloot,
        "petrecovery" => petrecovery,
        "petskillattack" => petskillattack,
        "petskillattack2" => petskillattack2,
        "petskillsupport" => petskillsupport,
        "petautobonus" => petautobonus,
        "petautobonus3" => petautobonus3,
        _ => return None,
    })
}

/// pet / catchpet {<item_id>{,<flag>}}: starts a capture with the lure (by default the item being used).
fn catchpet(rt: &Rt, st: &mut State) -> i32 {
    let command = st.funcname.clone();
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = rid2sd(rt, st, &command) else { return SCRIPT_CMD_FAILURE };
    let lure_id: u32;
    if st.hasdata(2) {
        lure_id = getnum(rt, st, 2) as u32;
        if crate::db::items::itemdb_exists(lure_id).is_none() {
            log::error!("buildin_{command}: Invalid lure item ID {lure_id}.");
            return SCRIPT_CMD_FAILURE;
        }
    } else {
        let itemid = crate::status::itemid(c, sd);
        if itemid == 0 {
            log::error!("buildin_{command}: Called outside of an item script without item id.");
            return SCRIPT_CMD_FAILURE;
        }
        lure_id = itemid;
    }
    let mut flag = PET_CATCH_NORMAL;
    if st.hasdata(3) {
        let val = getnum(rt, st, 3);
        if val < PET_CATCH_NORMAL as i32 || val >= PET_CATCH_MAX as i32 {
            log::error!("buildin_{command}: Invalid value '{val}' for flag.");
        }
        flag = val as u8;
    }
    super::pet_catch_process_start(c, sd, lure_id, flag);
    SCRIPT_CMD_SUCCESS
}

/// bpet / birthpet: the incubator's egg list (not when the player already has a pet).
fn birthpet(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "birthpet") else { return SCRIPT_CMD_SUCCESS };
    if super::pet_id_of(c, sd) != 0 {
        // do not send egg list, when you already have a pet
        return SCRIPT_CMD_SUCCESS;
    }
    super::clif::clif_sendegg(c, sd);
    SCRIPT_CMD_SUCCESS
}

/// makepet <mob id>: asks the char server for a new pet egg of that monster.
fn makepet(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = rid2sd(rt, st, "makepet") else { return SCRIPT_CMD_FAILURE };
    let mob_id = getnum(rt, st, 2) as u16;
    let Some(pet) = crate::db::misc::pet::pet_db().find(mob_id) else {
        log::error!("buildin_makepet: failed to create a pet with mob id {mob_id}");
        return SCRIPT_CMD_FAILURE;
    };
    let Some(mdb) = crate::db::mobs::mob_db().find(pet.class_ as u32) else { return SCRIPT_CMD_FAILURE };
    let char_id = crate::pc::get(c, sd).map(|p| p.char_id).unwrap_or(0);
    super::intif_create_pet(sd, char_id, pet.class_ as i16, mdb.lv as i16, pet.egg_id, 0, pet.intimate as i16, 100, 0, 1, &mdb.jname);
    SCRIPT_CMD_SUCCESS
}

/// script.hpp e_petinfo (PETINFO_*).
const PETINFO_ID: i32 = 0;
const PETINFO_CLASS: i32 = 1;
const PETINFO_NAME: i32 = 2;
const PETINFO_INTIMATE: i32 = 3;
const PETINFO_HUNGRY: i32 = 4;
const PETINFO_RENAMED: i32 = 5;
const PETINFO_LEVEL: i32 = 6;
const PETINFO_BLOCKID: i32 = 7;
const PETINFO_EGGID: i32 = 8;
const PETINFO_FOODID: i32 = 9;

/// getpetinfo <type>{,<char_id>}: a field of the player's pet ("null" / 0 without one).
fn getpetinfo(rt: &Rt, st: &mut State) -> i32 {
    let type_ = getnum(rt, st, 2);
    let pd = match rt.c {
        Some(c) => charid2sd(rt, st, 3, "getpetinfo").and_then(|sd| data::of_master(c, sd)),
        None => None,
    };
    let Some(pd) = pd else {
        if type_ == 2 {
            st.pushstr(b"null".to_vec());
        } else {
            st.pushint(0);
        }
        return SCRIPT_CMD_SUCCESS;
    };
    match type_ {
        PETINFO_ID => st.pushint(pd.pet.pet_id as i64),
        PETINFO_CLASS => st.pushint(pd.pet.class_ as i64),
        PETINFO_NAME => st.pushstr(crate::net::wire::latin1(&pd.pet.name)),
        PETINFO_INTIMATE => st.pushint(pd.pet.intimate as i64),
        PETINFO_HUNGRY => st.pushint(pd.pet.hungry as i64),
        PETINFO_RENAMED => st.pushint(pd.pet.rename_flag as i64),
        PETINFO_LEVEL => st.pushint(pd.pet.level as i64),
        PETINFO_BLOCKID => st.pushint(pd.id as i64),
        PETINFO_EGGID => st.pushint(pd.pet.egg_id as i64),
        PETINFO_FOODID => st.pushint(data::pet_db_of(&pd).map(|p| p.food_id as i64).unwrap_or(0)),
        _ => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// petskillbonus <bonus type>,<value>,<duration>,<delay>: the pet's skill bonus (pet_skill_bonus_timer switches it);
/// with pet_equip_required and no accessory the timer waits for pet_equipitem.
fn petskillbonus(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = rid2sd(rt, st, "petskillbonus") else { return SCRIPT_CMD_FAILURE };
    let Some(mut pd) = data::of_master(c, sd) else { return SCRIPT_CMD_FAILURE };
    match &pd.bonus {
        //Clear previous bonus
        Some(b) if b.timer != 0 => timer::delete(c, b.timer),
        _ => {} //init
    }
    let mut b = PetBonus {
        bonus_type: getnum(rt, st, 2) as u16,
        val: getnum(rt, st, 3) as u16,
        duration: getnum(rt, st, 4) as u16,
        delay: getnum(rt, st, 5) as u16,
        timer: 0,
    };
    if pd.skillbonus {
        pd.skillbonus = false; // waiting state
    }
    // wait for timer to start
    if super::pet_equip_required() && pd.pet.equip == 0 {
        b.timer = 0;
    } else {
        b.timer = timer::add(c, c.tick() + b.delay as Tick * 1000, Func::PetSkillBonusTimer, sd, 0);
    }
    pd.bonus = Some(b);
    data::put(c, &pd);
    SCRIPT_CMD_SUCCESS
}

/// petloot <max items>: the pet may carry up to max (1..MAX_PETLOOT_SIZE) looted items; what it carried before goes
/// to its master.
fn petloot(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "petloot") else { return SCRIPT_CMD_SUCCESS };
    let Some(pd) = data::of_master(c, sd) else { return SCRIPT_CMD_SUCCESS };
    let mut max = getnum(rt, st, 2);
    if max < 1 {
        max = 1; //Let'em loot at least 1 item.
    } else if max > MAX_PETLOOT_SIZE {
        max = MAX_PETLOOT_SIZE;
    }
    if pd.loot.is_some() {
        //Release whatever was there already and reallocate memory
        super::ai::pet_lootitem_drop(c, pd.id, Some(pd.master));
    }
    let Some(mut pd) = data::get(c, pd.id) else { return SCRIPT_CMD_SUCCESS };
    pd.loot = Some(PetLoot { items: Vec::new(), weight: 0, max: max as u16 });
    data::put(c, &pd);
    SCRIPT_CMD_SUCCESS
}

/// petrecovery <status type>,<delay>: the pet cures that status change of its master `delay` s after it starts.
fn petrecovery(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = rid2sd(rt, st, "petrecovery") else { return SCRIPT_CMD_FAILURE };
    let Some(mut pd) = data::of_master(c, sd) else { return SCRIPT_CMD_FAILURE };
    let sc = getnum(rt, st, 2);
    if sc <= crate::status::consts::SC_NONE || sc >= crate::status::consts::SC_MAX {
        log::error!("buildin_petrecovery: Invalid SC type: {sc}");
        return SCRIPT_CMD_FAILURE;
    }
    if let Some(r) = &pd.recovery {
        //Halt previous bonus
        if r.timer != 0 {
            timer::delete(c, r.timer);
        }
    }
    pd.recovery = Some(PetRecovery { sc_type: sc, delay: getnum(rt, st, 3) as u16, timer: 0 });
    data::put(c, &pd);
    SCRIPT_CMD_SUCCESS
}

/// The skill argument of petskillattack / petskillattack2 / petskillsupport: a name through skill_name2id, a number
/// through skill_get_index (rAthena stores the skill's index there, not its id).
fn skill_arg(rt: &Rt, st: &mut State) -> u16 {
    if isstring(rt, st, 2) {
        crate::db::skills::skill_name2id(&lat(&getstr(rt, st, 2)))
    } else {
        crate::db::skills::skill_get_index(getnum(rt, st, 2) as u16)
    }
}

/// petskillattack <skill id / "name">,<level>,<rate>,<bonusrate>.
fn petskillattack(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = rid2sd(rt, st, "petskillattack") else { return SCRIPT_CMD_FAILURE };
    let Some(mut pd) = data::of_master(c, sd) else { return SCRIPT_CMD_FAILURE };
    let id = skill_arg(rt, st);
    if id == 0 {
        log::error!("buildin_petskillattack: Invalid skill defined!");
        return SCRIPT_CMD_FAILURE;
    }
    let lv = getnum(rt, st, 3).min(crate::db::skills::skill_get_max(id)) as u16;
    pd.a_skill = Some(PetSkillAttack { id, damage: 0, lv, div_: 0, rate: getnum(rt, st, 4) as u16, bonusrate: getnum(rt, st, 5) as u16 });
    data::put(c, &pd);
    SCRIPT_CMD_SUCCESS
}

/// petskillattack2 <skill id / "name">,<damage>,<div>,<rate>,<bonusrate>: fixed damage at the skill's max level.
fn petskillattack2(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = rid2sd(rt, st, "petskillattack2") else { return SCRIPT_CMD_FAILURE };
    let Some(mut pd) = data::of_master(c, sd) else { return SCRIPT_CMD_FAILURE };
    let id = skill_arg(rt, st);
    if id == 0 {
        log::error!("buildin_petskillattack2: Invalid skill defined!");
        return SCRIPT_CMD_FAILURE;
    }
    pd.a_skill = Some(PetSkillAttack {
        id,
        damage: getnum(rt, st, 3) as u16, // Fixed damage
        lv: crate::db::skills::skill_get_max(id) as u16, // Adjust to max skill level
        div_: getnum(rt, st, 4) as u16,
        rate: getnum(rt, st, 5) as u16,
        bonusrate: getnum(rt, st, 6) as u16,
    });
    data::put(c, &pd);
    SCRIPT_CMD_SUCCESS
}

/// petskillsupport <skill id / "name">,<level>,<delay>,<hp>,<sp>: the pet's support skill; its timer starts `delay` s
/// from now (with pet_equip_required and no accessory it waits for pet_equipitem).
fn petskillsupport(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = rid2sd(rt, st, "petskillsupport") else { return SCRIPT_CMD_FAILURE };
    let Some(mut pd) = data::of_master(c, sd) else { return SCRIPT_CMD_FAILURE };
    let id = skill_arg(rt, st);
    if id == 0 {
        log::error!("buildin_petskillsupport: Invalid skill defined!");
        return SCRIPT_CMD_FAILURE;
    }
    if let Some(s) = &pd.s_skill {
        //Clear previous skill
        if s.timer != 0 {
            timer::delete(c, s.timer);
        }
    }
    let mut s = PetSkillSupport { id, lv: getnum(rt, st, 3) as u16, delay: getnum(rt, st, 4) as u16, hp: getnum(rt, st, 5) as u16, sp: getnum(rt, st, 6) as u16, timer: 0 };
    //Use delay as initial offset to avoid skill/heal exploits
    if super::pet_equip_required() && pd.pet.equip == 0 {
        s.timer = 0;
    } else {
        s.timer = timer::add(c, c.tick() + s.delay as Tick * 1000, Func::PetSkillSupportTimer, sd, 0);
    }
    pd.s_skill = Some(s);
    data::put(c, &pd);
    SCRIPT_CMD_SUCCESS
}

/// petautobonus / petautobonus2 "<bonus script>",<rate>,<duration>{,<flag>{,"<other script>"}}: an autobonus of the
/// pet (petautobonus2: when the owner is attacked).
fn petautobonus(rt: &Rt, st: &mut State) -> i32 {
    let command = st.funcname.clone();
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = rid2sd(rt, st, &command) else { return SCRIPT_CMD_FAILURE }; // No player attached
    if data::of_master(c, sd).is_none() {
        log::error!("buildin_{command}: Requires an active pet.");
        return SCRIPT_CMD_FAILURE; // No pet attached to player
    }
    let bonus_script = lat(&getstr(rt, st, 2));
    let rate = getnum(rt, st, 3) as i16;
    let dur = getnum(rt, st, 4) as u32;
    if rate == 0 || dur == 0 || bonus_script.is_empty() {
        return SCRIPT_CMD_FAILURE;
    }
    let bonus2 = command.eq_ignore_ascii_case("petautobonus2");
    let atk_type = if st.hasdata(5) { getnum(rt, st, 5) as u16 } else { 0 };
    let other_script = if st.hasdata(6) { lat(&getstr(rt, st, 6)) } else { String::new() };
    // script_add_petautobonus: the scripts are compiled on first use (petautobonus provider)
    super::bonus::pet_addautobonus(c, sd, if bonus2 { 2 } else { 1 }, &bonus_script, rate, dur, atk_type, &other_script, false);
    SCRIPT_CMD_SUCCESS
}

/// A script constant's value (UPET_*).
fn k(name: &str) -> i32 {
    crate::data::constant(name).map(|v| v as i32).unwrap_or(i32::MIN)
}

/// script.cpp buildin_getunitdata, BL_PET: the pet's data into array[UPET_*] (getunitdata_sub).
#[allow(clippy::too_many_arguments)]
pub fn getunitdata_pet(rt: &Rt, st: &mut State, u: &crate::unit::Unit, sd: Option<u32>, id: i32, name: &[u8], rf: &RegRef) {
    let Some(c) = rt.c else { return };
    let Some(pd) = data::get(c, u.id) else { return };
    let s = &pd.status;
    let ud = &u.ud;
    let vals: Vec<(&str, i64)> = vec![
        ("UPET_SIZE", s.size as i64),
        ("UPET_LEVEL", pd.pet.level as i64),
        ("UPET_HP", s.hp as i64),
        ("UPET_MAXHP", s.max_hp as i64),
        ("UPET_MASTERAID", pd.pet.account_id as i64),
        ("UPET_MAPID", u.map as i64),
        ("UPET_X", u.x as i64),
        ("UPET_Y", u.y as i64),
        ("UPET_HUNGER", pd.pet.hungry as i64),
        ("UPET_INTIMACY", pd.pet.intimate as i64),
        ("UPET_SPEED", s.speed as i64),
        ("UPET_LOOKDIR", ud.dir as i64),
        ("UPET_CANMOVETICK", ud.canmove_tick),
        ("UPET_STR", s.str as u16 as i64),
        ("UPET_AGI", s.agi as u16 as i64),
        ("UPET_VIT", s.vit as u16 as i64),
        ("UPET_INT", s.int_ as u16 as i64),
        ("UPET_DEX", s.dex as u16 as i64),
        ("UPET_LUK", s.luk as u16 as i64),
        ("UPET_DMGIMMUNE", ud.immune_attack as i64),
        ("UPET_ATKRANGE", s.rhw.range as i64),
        ("UPET_ATKMIN", s.rhw.atk as i64),
        ("UPET_ATKMAX", s.rhw.atk2 as i64),
        ("UPET_MATKMIN", s.matk_min as i64),
        ("UPET_MATKMAX", s.matk_max as i64),
        ("UPET_DEF", s.def as i64),
        ("UPET_MDEF", s.mdef as i64),
        ("UPET_HIT", s.hit as i64),
        ("UPET_FLEE", s.flee as i64),
        ("UPET_PDODGE", s.flee2 as i64),
        ("UPET_CRIT", s.cri as i64),
        ("UPET_RACE", s.race as i64),
        ("UPET_ELETYPE", s.def_ele as i64),
        ("UPET_ELELEVEL", s.ele_lv as i64),
        ("UPET_AMOTION", s.amotion as i16 as i64),
        ("UPET_ADELAY", s.adelay as i16 as i64),
        ("UPET_DMOTION", s.dmotion as i16 as i64),
        ("UPET_GROUP_ID", ud.group_id as i64),
    ];
    for (key, v) in vals {
        crate::script::reg::set_reg_num(rt, st, sd, id, k(key) as u32, name, v, rf);
    }
}

/// script.cpp buildin_setunitdata, BL_PET: one UPET_* field changes, then the owner gets the pet's status
/// (clif_send_petstatus).
pub fn setunitdata_pet(c: &crate::world::Ctx, id: u32, t: i32, value: i32, mapname: Option<&[u8]>) -> i32 {
    use crate::status::defs::SCO_NONE;
    let Some(mut pd) = data::get(c, id) else { return SCRIPT_CMD_FAILURE };
    let Some(u) = crate::unit::get(c, id) else { return SCRIPT_CMD_FAILURE };
    let misc = |pd: &mut data::PetData| {
        let lv = pd.pet.level as i32;
        super::status::status_calc_misc_pet(&mut pd.status, lv);
    };
    if t == k("UPET_SIZE") {
        pd.status.size = value as u8;
    } else if t == k("UPET_LEVEL") {
        pd.pet.level = value as u16 as i16;
    } else if t == k("UPET_HP") || t == k("UPET_MAXHP") {
        // pd->status.hp = pd->status.max_hp = value; status_set_hp / status_set_maxhp then change nothing
        pd.status.hp = value as u32;
        pd.status.max_hp = value as u32;
    } else if t == k("UPET_MASTERAID") {
        pd.pet.account_id = value as u32;
    } else if t == k("UPET_MAPID") {
        let v = match mapname {
            Some(n) => crate::data::maps().id_of(&crate::net::wire::from_latin1(n)).map(|m| m as i32).unwrap_or(-1),
            None => value,
        };
        data::put(c, &pd);
        super::ai::unit_warp(c, id, v as i16 as i32, 0, 0, crate::unit::CLR_TELEPORT);
        pd = data::get(c, id).unwrap_or(pd);
    } else if t == k("UPET_X") || t == k("UPET_Y") {
        let (x, y) = if t == k("UPET_X") { (value as i16, u.y) } else { (u.x, value as i16) };
        data::put(c, &pd);
        if !crate::unit::walktoxy(c, id, x, y, 2) {
            crate::unit::movepos(c, id, x, y, 0, false);
        }
        pd = data::get(c, id).unwrap_or(pd);
    } else if t == k("UPET_HUNGER") {
        pd.pet.hungry = (value as i16).clamp(0, 100);
        data::put(c, &pd);
        if crate::pc::get(c, pd.pet.account_id).is_some() {
            super::clif::clif_send_petdata(c, Some(pd.pet.account_id), &pd, super::clif::CHANGESTATEPET_HUNGER);
        }
    } else if t == k("UPET_INTIMACY") {
        data::put(c, &pd);
        super::pet_set_intimate(c, id, value as u32 as i32);
        pd = data::get(c, id).unwrap_or(pd);
        if crate::pc::get(c, pd.pet.account_id).is_some() {
            super::clif::clif_send_petdata(c, Some(pd.pet.account_id), &pd, super::clif::CHANGESTATEPET_INTIMACY);
        }
    } else if t == k("UPET_SPEED") {
        pd.status.speed = value as u16;
        misc(&mut pd);
    } else if t == k("UPET_LOOKDIR") {
        data::put(c, &pd);
        if let Some(mut u) = crate::unit::get(c, id) {
            crate::unit::setdir(c, &mut u, value as u8, true);
            crate::unit::save(c, &u);
        }
        pd = data::get(c, id).unwrap_or(pd);
    } else if t == k("UPET_CANMOVETICK") {
        if let Some(mut u) = crate::unit::get(c, id) {
            u.ud.canmove_tick = if value > 0 { value as u32 as i64 } else { 0 };
            crate::unit::save(c, &u);
        }
    } else if t == k("UPET_STR") {
        pd.status.str = value as u16 as i16;
        misc(&mut pd);
    } else if t == k("UPET_AGI") {
        pd.status.agi = value as u16 as i16;
        misc(&mut pd);
    } else if t == k("UPET_VIT") {
        pd.status.vit = value as u16 as i16;
        misc(&mut pd);
    } else if t == k("UPET_INT") {
        pd.status.int_ = value as u16 as i16;
        misc(&mut pd);
    } else if t == k("UPET_DEX") {
        pd.status.dex = value as u16 as i16;
        misc(&mut pd);
    } else if t == k("UPET_LUK") {
        pd.status.luk = value as u16 as i16;
        misc(&mut pd);
    } else if t == k("UPET_DMGIMMUNE") {
        if let Some(mut u) = crate::unit::get(c, id) {
            u.ud.immune_attack = value > 0;
            crate::unit::save(c, &u);
        }
    } else if t == k("UPET_ATKRANGE") {
        pd.status.rhw.range = value as u16;
    } else if t == k("UPET_ATKMIN") {
        pd.status.rhw.atk = value as u16;
    } else if t == k("UPET_ATKMAX") {
        pd.status.rhw.atk2 = value as u16;
    } else if t == k("UPET_MATKMIN") {
        pd.status.matk_min = value as u16;
    } else if t == k("UPET_MATKMAX") {
        pd.status.matk_max = value as u16;
    } else if t == k("UPET_DEF") {
        pd.status.def = value as i16;
    } else if t == k("UPET_MDEF") {
        pd.status.mdef = value as i16;
    } else if t == k("UPET_HIT") {
        pd.status.hit = value as i16;
    } else if t == k("UPET_FLEE") {
        pd.status.flee = value as i16;
    } else if t == k("UPET_PDODGE") {
        pd.status.flee2 = value as i16;
    } else if t == k("UPET_CRIT") {
        pd.status.cri = value as i16;
    } else if t == k("UPET_RACE") {
        pd.status.race = value as u8;
    } else if t == k("UPET_ELETYPE") {
        pd.status.def_ele = value as u8;
    } else if t == k("UPET_ELELEVEL") {
        pd.status.ele_lv = value as u8;
    } else if t == k("UPET_AMOTION") {
        pd.status.amotion = value as i16 as u16;
    } else if t == k("UPET_ADELAY") {
        pd.status.adelay = value as i16 as u16;
    } else if t == k("UPET_DMOTION") {
        pd.status.dmotion = value as i16 as u16;
    } else if t == k("UPET_GROUP_ID") {
        if let Some(mut u) = crate::unit::get(c, id) {
            u.ud.group_id = value;
            crate::unit::save(c, &u);
            crate::unit_npc::unit_refresh(c, &u, false);
        }
    } else {
        log::error!("buildin_setunitdata: Unknown data identifier {t} for BL_PET.");
        return SCRIPT_CMD_FAILURE;
    }
    data::put(c, &pd);
    let _ = SCO_NONE;
    // Client information update
    super::clif::clif_send_petstatus(c, pd.master, &pd);
    SCRIPT_CMD_SUCCESS
}

/// petautobonus3 "<bonus script>",<rate>,<duration>,<skill id / "name">{,"<other script>"}: an autobonus of the pet
/// when its owner uses that skill.
fn petautobonus3(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = rid2sd(rt, st, "petautobonus3") else { return SCRIPT_CMD_SUCCESS }; // No player attached
    if data::of_master(c, sd).is_none() {
        log::error!("buildin_petautobonus3: Requires an active pet.");
        return SCRIPT_CMD_FAILURE; // No pet attached to player
    }
    let bonus_script = lat(&getstr(rt, st, 2));
    let rate = getnum(rt, st, 3) as i16;
    let dur = getnum(rt, st, 4) as u32;
    if rate == 0 || dur == 0 || bonus_script.is_empty() {
        return SCRIPT_CMD_SUCCESS;
    }
    let skill_id: u16;
    if isstring(rt, st, 5) {
        let name = lat(&getstr(rt, st, 5));
        skill_id = crate::db::skills::skill_name2id(&name);
        if skill_id == 0 {
            log::error!("buildin_petautobonus3: Invalid skill name {name} passed to item bonus. Skipping.");
            return SCRIPT_CMD_FAILURE;
        }
    } else {
        skill_id = getnum(rt, st, 5) as u16;
        if crate::db::skills::skill_get_index(skill_id) == 0 {
            log::error!("buildin_petautobonus3: Invalid skill ID {skill_id} passed to item bonus. Skipping.");
            return SCRIPT_CMD_FAILURE;
        }
    }
    let other_script = if st.hasdata(6) { lat(&getstr(rt, st, 6)) } else { String::new() };
    super::bonus::pet_addautobonus(c, sd, 3, &bonus_script, rate, dur, skill_id, &other_script, true);
    SCRIPT_CMD_SUCCESS
}
