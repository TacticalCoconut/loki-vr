//! map/mercenary.cpp (rAthena e985006): mercenaries. Creation (mercenary_create → the char server → mercenary_recv_data),
//! the contract timer (merc_contract_init / merc_contract_end / mercenary_contract_stop / mercenary_get_lifetime),
//! deletion (mercenary_delete: contract over, killed, fired, ran away), saving (mercenary_save), the owner's guild
//! counters (mercenary_get_guild, faith and calls), kills (mercenary_kills / mercenary_killbonus), heal / death
//! notifications and mercenary_checkskill; and the map-server side of the inter link (intif_mercenary_*). There is no
//! server-side AI: the client drives a mercenary (CZ_REQUEST_MOVENPC / ACTNPC / MOVETOOWNER, its skills).
//!
//! Submodules: `data` (tables), `clif` (the mercenary packets and requests), `skill` (the mercenary parts of
//! skill.cpp: cooldowns, skill_isNotOk_mercenary, the skill requests), `buildins` (the script commands).

pub mod buildins;
pub mod clif;
pub mod data;
pub mod skill;

use crate::companion::MercElemInter;
use crate::int_mercenary::SMercenary;
use crate::intif::{self, Inter};
use crate::map::BL_MER;
use crate::status::consts::*;
use crate::timer::{self, Func};
use crate::unit::{self, CLR_OUTSIGHT};
use crate::world::Ctx;
use data::*;

/// clif.hpp e_clif_messages MSI_MER_FINISH ("The mercenary contract has expired.") / MSI_MER_DIE ("The mercenary has
/// died.").
pub const MSI_MER_FINISH: u16 = 1266;
pub const MSI_MER_DIE: u16 = 1267;

// ---------------------------------------------------------------------------------------------- intif

/// intif.cpp intif_mercenary_create (0x3070).
pub fn intif_mercenary_create(merc: SMercenary) {
    intif::send(Inter::MercElem(MercElemInter::MercenaryCreate { merc }));
}

/// intif.cpp intif_mercenary_request (0x3071).
pub fn intif_mercenary_request(merc_id: i32, char_id: u32) {
    intif::send(Inter::MercElem(MercElemInter::MercenaryRequest { merc_id, char_id }));
}

/// intif.cpp intif_mercenary_delete (0x3072).
pub fn intif_mercenary_delete(merc_id: i32) {
    intif::send(Inter::MercElem(MercElemInter::MercenaryDelete { merc_id }));
}

/// intif.cpp intif_mercenary_save (0x3073).
pub fn intif_mercenary_save(merc: SMercenary) {
    intif::send(Inter::MercElem(MercElemInter::MercenarySave { merc }));
}

// ---------------------------------------------------------------------------------------------- mercenary.cpp

/// mercenary.cpp mercenary_create: asks the char server to create a mercenary of `class_` for `aid` with full HP / SP
/// and `lifetime` ms of contract. False: unknown class.
pub fn mercenary_create(c: &Ctx, aid: u32, class_: i32, lifetime: u32) -> bool {
    let Some(db) = crate::db::misc::mercenary::mercenary_db().find(class_) else {
        log::error!("mercenary_create: Unknown mercenary class {class_}.");
        return false;
    };
    let Some(p) = crate::pc::get(c, aid) else { return false };
    let merc = SMercenary {
        char_id: p.char_id,
        class_: class_ as i16,
        hp: db.status.max_hp as i32,
        sp: db.status.max_sp as i32,
        life_time: lifetime as i64,
        ..Default::default()
    };
    // Request Char Server to create this mercenary
    intif_mercenary_create(merc);
    true
}

/// mercenary.cpp mercenary_get_lifetime: the contract time left (0 without a contract timer).
pub fn mercenary_get_lifetime(c: &Ctx, md: &MercenaryData) -> i64 {
    if md.contract_timer == 0 {
        return 0;
    }
    timer::due(c, md.contract_timer).map(|due| due - c.tick()).unwrap_or(0)
}

/// mercenary.cpp mercenary_get_guild: the guild of the mercenary's class (enum e_MercGuildType).
pub fn mercenary_get_guild(md: &MercenaryData) -> i32 {
    let Some(db) = db_of(md) else { return NONE_MERC_GUILD };
    let class_ = db.class_;
    if (MERID_MER_ARCHER01..=MERID_MER_ARCHER10).contains(&class_) {
        return ARCH_MERC_GUILD;
    }
    if (MERID_MER_LANCER01..=MERID_MER_LANCER10).contains(&class_) {
        return SPEAR_MERC_GUILD;
    }
    if (MERID_MER_SWORDMAN01..=MERID_MER_SWORDMAN10).contains(&class_) {
        return SWORD_MERC_GUILD;
    }
    NONE_MERC_GUILD
}

/// mercenary.cpp mercenary_get_faith: the master's faith with the mercenary's guild.
pub fn mercenary_get_faith(c: &Ctx, md: &MercenaryData) -> i32 {
    if db_of(md).is_none() || crate::pc::get(c, md.master).is_none() {
        return 0;
    }
    let o = owner(c, md.master);
    match mercenary_get_guild(md) {
        ARCH_MERC_GUILD => o.arch_faith,
        SPEAR_MERC_GUILD => o.spear_faith,
        SWORD_MERC_GUILD => o.sword_faith,
        _ => 0,
    }
}

/// mercenary.cpp mercenary_set_faith: adds `value` to the master's faith with the mercenary's guild (0..SHRT_MAX) and
/// shows it (SP_MERCFAITH).
pub fn mercenary_set_faith(c: &Ctx, md: &MercenaryData, value: i32) {
    if db_of(md).is_none() || crate::pc::get(c, md.master).is_none() {
        return;
    }
    let mut o = owner(c, md.master);
    let faith = match mercenary_get_guild(md) {
        ARCH_MERC_GUILD => &mut o.arch_faith,
        SPEAR_MERC_GUILD => &mut o.spear_faith,
        SWORD_MERC_GUILD => &mut o.sword_faith,
        _ => return,
    };
    *faith = faith.wrapping_add(value).clamp(0, i16::MAX as i32);
    put_owner(c, &o);
    clif::clif_mercenary_updatestatus(c, md.master, SP_MERCFAITH);
}

/// mercenary.cpp mercenary_get_calls: the master's calls of the mercenary's guild.
pub fn mercenary_get_calls(c: &Ctx, md: &MercenaryData) -> i32 {
    if db_of(md).is_none() || crate::pc::get(c, md.master).is_none() {
        return 0;
    }
    let o = owner(c, md.master);
    match mercenary_get_guild(md) {
        ARCH_MERC_GUILD => o.arch_calls,
        SPEAR_MERC_GUILD => o.spear_calls,
        SWORD_MERC_GUILD => o.sword_calls,
        _ => 0,
    }
}

/// mercenary.cpp mercenary_set_calls: adds `value` to the master's calls of the mercenary's guild (0..INT_MAX).
pub fn mercenary_set_calls(c: &Ctx, md: &MercenaryData, value: i32) {
    if db_of(md).is_none() || crate::pc::get(c, md.master).is_none() {
        return;
    }
    let mut o = owner(c, md.master);
    let calls = match mercenary_get_guild(md) {
        ARCH_MERC_GUILD => &mut o.arch_calls,
        SPEAR_MERC_GUILD => &mut o.spear_calls,
        SWORD_MERC_GUILD => &mut o.sword_calls,
        _ => return,
    };
    *calls = calls.wrapping_add(value).clamp(0, i32::MAX);
    put_owner(c, &o);
}

/// mercenary.cpp mercenary_save: HP / SP / the contract time left and the running cooldowns (md->scd in its iteration
/// order, their time left) go to the char server.
pub fn mercenary_save(c: &Ctx, id: u32) {
    let Some(mut md) = get(c, id) else { return };
    md.mercenary.hp = md.battle_status.hp as i32;
    md.mercenary.sp = md.battle_status.sp as i32;
    md.mercenary.life_time = mercenary_get_lifetime(c, &md);
    // Clear skill cooldown array.
    md.mercenary.scd = vec![crate::int_mercenary::SkillCooldownData::default(); crate::int_mercenary::MAX_SKILLCOOLDOWN];
    // Store current cooldown entries.
    let mut count = 0usize;
    let tick = c.tick();
    for e in md.scd_entries() {
        // timer == nullptr || timer->func != skill_blockmerc_end || DIFF_TICK(timer->tick, tick) < 0: skipped
        let Some(t) = c.db().timer().id().find(e.timer) else { continue };
        if t.func != Func::SkillBlockmercEnd as u16 || t.due - tick < 0 {
            continue;
        }
        if count < md.mercenary.scd.len() {
            md.mercenary.scd[count] = crate::int_mercenary::SkillCooldownData { skill_id: e.skill_id, tick: t.due - tick };
        }
        count += 1;
    }
    put(c, &md);
    intif_mercenary_save(md.mercenary.clone());
}

use crate::timer::timer as _;

/// mercenary.cpp merc_contract_end (Func::MercContractEnd; target = the master's account id): the contract is over.
pub fn merc_contract_end(c: &Ctx, tid: u64, aid: u32) -> i32 {
    if crate::pc::get(c, aid).is_none() {
        return 1;
    }
    let Some(mut md) = of_master(c, aid) else { return 1 };
    if md.contract_timer != tid {
        log::error!("merc_contract_end {} != {}.", md.contract_timer, tid);
        return 0;
    }
    md.contract_timer = 0;
    put(c, &md);
    mercenary_delete(c, md.id, 0); // Mercenary soldier's duty hour is over.
    0
}

/// mercenary.cpp mercenary_delete: the contract ends (reply 0: contract over, +1 faith and MSI_MER_FINISH; 1: killed,
/// -1 faith and MSI_MER_DIE; 2: fired; 3: ran away), Devotion on the master ends, and the mercenary leaves the map
/// (unit_remove_map frees it: it has no time left). Without a master it is freed at once.
pub fn mercenary_delete(c: &Ctx, id: u32, reply: i32) -> i32 {
    let Some(mut md) = get(c, id) else { return 0 };
    md.mercenary.life_time = 0;
    put(c, &md);
    mercenary_contract_stop(c, id);
    let Some(mut md) = get(c, id) else { return 0 };
    let Some(sd) = unit::get(c, md.master).filter(|_| crate::pc::get(c, md.master).is_some()) else {
        return crate::companion::unit::unit_free(c, id, CLR_OUTSIGHT);
    };
    if md.devotion_flag {
        md.devotion_flag = false;
        put(c, &md);
        crate::status::sc::status_change_end(c, sd.id, SC_DEVOTION, 0);
    }
    let Some(md) = get(c, id) else { return 0 };
    match reply {
        0 => {
            // +1 Loyalty on Contract ends.
            mercenary_set_faith(c, &md, 1);
            crate::clif_items::msg(c, &sd, MSI_MER_FINISH);
        }
        1 => {
            // -1 Loyalty on Mercenary killed
            mercenary_set_faith(c, &md, -1);
            crate::clif_items::msg(c, &sd, MSI_MER_DIE);
        }
        _ => {}
    }
    unit::remove_map(c, id, CLR_OUTSIGHT) as i32
}

/// mercenary.cpp mercenary_contract_stop: the contract timer ends.
pub fn mercenary_contract_stop(c: &Ctx, id: u32) {
    let Some(mut md) = get(c, id) else { return };
    if md.contract_timer != 0 {
        timer::delete_checked(c, md.contract_timer, Func::MercContractEnd);
    }
    md.contract_timer = 0;
    put(c, &md);
}

/// mercenary.cpp merc_contract_init: the contract timer starts (life_time from now), regen is unblocked.
fn merc_contract_init(c: &Ctx, md: &mut MercenaryData) {
    if md.contract_timer == 0 {
        md.contract_timer = timer::add(c, c.tick() + md.mercenary.life_time, Func::MercContractEnd, md.master, 0);
    }
    md.regen.block = 0;
}

/// mercenary.cpp mercenary_recv_data: the char server created or loaded a mercenary. A new one gets its object
/// (an NPC id, the view data, the master's direction, a cell next to him, regen ticks, map_addiddb, the first status
/// calculation, the contract timer); an existing one takes the new data. A first mercenary of the session counts a
/// call; with the master on a map it is spawned and shown (ZC_MER_INIT, ZC_MER_SKILLINFO_LIST), and the saved
/// cooldowns start again.
pub fn mercenary_recv_data(c: &Ctx, merc: SMercenary, flag: bool) -> bool {
    let tick = c.tick();
    let Some(aid) = crate::pc::charid2sd(c, merc.char_id).map(|u| u.id) else { return false };
    let db = crate::db::misc::mercenary::mercenary_db().find(merc.class_ as i32);
    if !flag || db.is_none() {
        // Not created - loaded - DB info
        set_mer_id(c, aid, 0);
        return false;
    }
    let Some(sdu) = unit::get(c, aid) else { return false };
    let id = match of_master(c, aid) {
        None => {
            let id = crate::mob::spawn::npc_get_new_npc_id(c);
            let mut u = crate::unit::Unit {
                id,
                kind: BL_MER,
                map: sdu.map,
                x: sdu.x,
                y: sdu.y,
                on_map: false,
                name: String::new(),
                vd: Default::default(),
                ud: Default::default(),
                sc: Default::default(),
                speed: crate::status::defs::DEFAULT_WALK_SPEED as u16,
                mode: 0,
                level: 1,
                char_id: 0,
                head_dir: 0,
                font: 0,
                manner: 0,
                karma: false,
                guild_id: 0,
                emblem_id: 0,
                hp: 0,
                max_hp: 0,
                dead: false,
                size: 0,
                boss: 0,
                invisible: false,
                npc_owner_char: 0,
            };
            // unit_dataset: the unit_data starts empty but for the ticks (now) and the sub-cell (8, 8); the direction of
            // the master (md->ud.dir = sd->ud.dir)
            u.ud.attackabletime = tick;
            u.ud.canact_tick = tick;
            u.ud.canmove_tick = tick;
            u.ud.endure_tick = tick;
            u.ud.sx = 8;
            u.ud.sy = 8;
            u.ud.dir = sdu.ud.dir;
            c.db().unit().insert(u);
            let md = MercenaryData {
                id,
                master: aid,
                db_class: merc.class_ as i32,
                mercenary: merc.clone(),
                base_status: Default::default(),
                battle_status: Default::default(),
                regen: Default::default(),
                // Ticks need to be initialized before adding bl to map_addiddb
                regen_tick_hp: tick,
                regen_tick_sp: tick,
                scd: Vec::new(),
                scd_order: Vec::new(),
                scd_buckets: 1,
                scd_next_resize: 0,
                masterteleport_timer: 0,
                contract_timer: 0,
                devotion_flag: false,
            };
            put(c, &md);
            crate::companion::status::status_set_viewdata(c, id, md.mercenary.class_ as i32);
            // unit_calc_pos(md, sd->x, sd->y, sd->ud.dir); md->x = md->ud.to_x; md->y = md->ud.to_y
            crate::pet::ai::unit_calc_pos(c, id, sdu.x as i32, sdu.y as i32, sdu.ud.dir);
            if let Some(mut u) = unit::get(c, id) {
                u.x = u.ud.to_x;
                u.y = u.ud.to_y;
                unit::save(c, &u);
            }
            // map_addiddb: the mercenary is in regen_db (natural heal)
            crate::status::hp::ensure_natural_heal_timer(c);
            crate::status::calc_bl(c, id, crate::db::misc::status::status_db().get_scb_all(), crate::status::SCO_FIRST);
            if let Some(mut md) = get(c, id) {
                md.contract_timer = 0;
                md.masterteleport_timer = 0;
                merc_contract_init(c, &mut md);
                put(c, &md);
            }
            id
        }
        Some(mut md) => {
            md.mercenary = merc.clone();
            put(c, &md);
            md.id
        }
    };
    if mer_id_of(c, aid) == 0 {
        if let Some(md) = get(c, id) {
            mercenary_set_calls(c, &md, 1);
        }
    }
    set_mer_id(c, aid, merc.mercenary_id);
    let on_map = unit::get(c, id).map(|u| u.on_map).unwrap_or(true);
    if !on_map && unit::get(c, aid).map(|u| u.on_map).unwrap_or(false) {
        let Some(mut u) = unit::get(c, id) else { return false };
        if !unit::addblock(c, &mut u) {
            return false;
        }
        crate::clif::spawn(c, &u, false);
        clif::clif_mercenary_info(c, aid);
        clif::clif_mercenary_skillblock(c, aid);
    }
    // Apply any active skill cooldowns.
    let scd = get(c, id).map(|md| md.mercenary.scd.clone()).unwrap_or_default();
    for e in scd {
        skill::skill_blockmerc_start(c, id, e.skill_id, e.tick);
    }
    true
}

/// mercenary.cpp mercenary_heal: the master sees the mercenary's new HP / SP.
pub fn mercenary_heal(c: &Ctx, master: u32, hp: i32, sp: i32) {
    if crate::pc::get(c, master).is_none() {
        return;
    }
    if hp != 0 {
        clif::clif_mercenary_updatestatus(c, master, SP_HP);
    }
    if sp != 0 {
        clif::clif_mercenary_updatestatus(c, master, SP_SP);
    }
}

/// mercenary.cpp mercenary_dead: the mercenary is deleted (killed); false for status_damage ("death cancelled").
pub fn mercenary_dead(c: &Ctx, id: u32) -> bool {
    mercenary_delete(c, id, 1);
    false
}

/// mercenary.cpp mercenary_killbonus: one of five bonuses for 5 minutes (rnd_value(1, 5) as its level).
fn mercenary_killbonus(c: &Ctx, id: u32) {
    let scs = [SC_MERC_FLEEUP, SC_MERC_ATKUP, SC_MERC_HPUP, SC_MERC_SPUP, SC_MERC_HITUP];
    // util::vector_random(scs): rnd_value(0, size - 1)
    let t = scs[c.rnd_value(0, scs.len() as i32 - 1) as usize];
    let v = c.rnd_value(1, 5);
    if let Some(u) = unit::get(c, id) {
        crate::status::sc::sc_start(c, Some(&u), &u, t, 100, v, 300000); //Bonus lasts for 5 minutes
    }
}

/// mercenary.cpp mercenary_kills: one more kill; every 50th adds faith and a kill bonus; the master sees the count.
pub fn mercenary_kills(c: &Ctx, id: u32) {
    let Some(mut md) = get(c, id) else { return };
    if md.mercenary.kill_count <= (i32::MAX - 1) as u32 {
        //safe cap to INT_MAX
        md.mercenary.kill_count += 1;
    }
    put(c, &md);
    if md.mercenary.kill_count % 50 == 0 {
        mercenary_set_faith(c, &md, 1);
        mercenary_killbonus(c, id);
    }
    if crate::pc::get(c, md.master).is_some() {
        clif::clif_mercenary_updatestatus(c, md.master, SP_MERCKILLS);
    }
}

/// mercenary.cpp mercenary_checkskill: the level of the skill in the mercenary's database skills, or 0.
pub fn mercenary_checkskill(md: &MercenaryData, skill_id: u16) -> u16 {
    db_of(md).map(|d| d.checkskill(skill_id)).unwrap_or(0)
}

/// mercenary_checkskill for a unit id (0 when it is no mercenary).
pub fn checkskill_id(c: &Ctx, id: u32, skill_id: u16) -> u16 {
    get(c, id).map(|md| mercenary_checkskill(&md, skill_id)).unwrap_or(0)
}

// ---------------------------------------------------------------------------------------------- the master's hooks

/// char.cpp char_mmo_char_fromsql's mercenary_owner_fromsql for a character entering the world: its sd->status
/// mercenary fields (zero without a `mercenary_owner` row).
pub fn pc_authok(c: &Ctx, aid: u32, char_id: u32) {
    let mut o = PcMercenary { account_id: aid, char_id, ..Default::default() };
    if let Some(r) = crate::int_mercenary::mercenary_owner_fromsql(c, char_id) {
        o.mer_id = r.merc_id;
        o.arch_calls = r.arch_calls;
        o.arch_faith = r.arch_faith;
        o.spear_calls = r.spear_calls;
        o.spear_faith = r.spear_faith;
        o.sword_calls = r.sword_calls;
        o.sword_faith = r.sword_faith;
    }
    if c.db().pc_mercenary().account_id().find(aid).is_some() {
        c.db().pc_mercenary().account_id().update(o);
    } else {
        c.db().pc_mercenary().insert(o);
    }
}

/// pc.cpp pc_reg_received: `if( sd->status.mer_id > 0 ) intif_mercenary_request(sd->status.mer_id, sd->status.char_id)`,
/// and the char server's answer (mapif_parse_mercenary_load → mercenary_recv_data). rAthena gets that answer right
/// after the pet's and the homunculus' and before the skill cooldowns; the module's login handles those answers in
/// place in that order (pc.rs authok), so this request is answered in place too, at its turn.
pub fn pc_reg_received(c: &Ctx, aid: u32) {
    let o = owner(c, aid);
    if o.mer_id > 0 {
        let (flag, merc) = crate::int_mercenary::mapif_mercenary_load(c, o.mer_id, o.char_id);
        mercenary_recv_data(c, merc, flag);
    }
}

/// char.cpp char_mmo_char_tosql's mercenary part (at every chrif_save): `mercenary_owner_tosql` when a field differs
/// from the char server's copy (a character that never had one keeps no row while everything is 0).
pub fn chrif_save_owner(c: &Ctx, aid: u32) {
    let Some(o) = c.db().pc_mercenary().account_id().find(aid) else { return };
    let row = crate::int_mercenary::MercenaryOwner {
        char_id: o.char_id,
        merc_id: o.mer_id,
        arch_calls: o.arch_calls,
        arch_faith: o.arch_faith,
        spear_calls: o.spear_calls,
        spear_faith: o.spear_faith,
        sword_calls: o.sword_calls,
        sword_faith: o.sword_faith,
    };
    let cp = crate::int_mercenary::mercenary_owner_fromsql(c, o.char_id).unwrap_or(crate::int_mercenary::MercenaryOwner { char_id: o.char_id, ..Default::default() });
    if cp != row {
        crate::int_mercenary::mercenary_owner_tosql(c, &row);
    }
}

/// chrif.cpp chrif_save: `if( sd->md && mercenary_get_lifetime(sd->md) > 0 ) mercenary_save(sd->md)`.
pub fn chrif_save(c: &Ctx, aid: u32) {
    if let Some(md) = of_master(c, aid) {
        if mercenary_get_lifetime(c, &md) > 0 {
            mercenary_save(c, md.id);
        }
    }
}

/// map_quit / unit_free_pc: the owner's map-side row goes with the character.
pub fn on_pc_free(c: &Ctx, aid: u32) {
    c.db().pc_mercenary().account_id().delete(aid);
}

/// pc.cpp pc_dead: `if( sd->md ) mercenary_delete(sd->md, 3)` (Your mercenary soldier has ran away).
pub fn pc_dead(c: &Ctx, aid: u32) {
    if let Some(md) = of_master(c, aid) {
        mercenary_delete(c, md.id, 3);
    }
}

/// clif.cpp clif_parse_LoadEndAck's mercenary part (clif.cpp:10884-10891): back on the map with its spawn packets,
/// ZC_MER_INIT, ZC_MER_SKILLINFO_LIST, and the master's speed (status_calc_bl SCB_SPEED).
pub fn load_end_ack(c: &Ctx, aid: u32) {
    let Some(md) = of_master(c, aid) else { return };
    let Some(mut u) = unit::get(c, md.id) else { return };
    if !unit::addblock(c, &mut u) {
        return;
    }
    crate::clif::spawn(c, &u, false);
    clif::clif_mercenary_info(c, aid);
    clif::clif_mercenary_skillblock(c, aid);
    crate::status::calc_bl(c, md.id, 1u64 << crate::status::consts::SCB_SPEED, crate::status::SCO_NONE); // Mercenary mimic their master's speed on each map change
}

use data::pc_mercenary;
use spacetimedb::Table;
use crate::unit::unit as _;

/// `bl->type == BL_MER && ((TBL_MER*)bl)->devotion_flag && (tsd = ((TBL_MER*)bl)->master)` (status.cpp's Devotion
/// arms): the master of a mercenary that devotes him.
pub fn devotion_master(c: &Ctx, id: u32) -> Option<crate::unit::Unit> {
    let md = get(c, id).filter(|md| md.devotion_flag)?;
    crate::pc::get(c, md.master)?;
    unit::get(c, md.master)
}
