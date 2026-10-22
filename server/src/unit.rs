//! map/unit.cpp: every visible object (rAthena's block_list + unit_data + view_data) is one `unit` row; walking,
//! direction, stopping and removal from the map are ported from unit.cpp.

use spacetimedb::{table, SpacetimeType};

use crate::clif;
use crate::map::{self, CellChk, BL_MOB, BL_NPC, BL_PC};
use crate::path::{self, DIRX, DIRY, MOVE_COST, MOVE_DIAGONAL_COST};
use crate::timer::{self, Func};
use crate::world::{Ctx, Tick};

/// e_option bits (status.hpp) the unit code tests.
pub const OPTION_HIDE: i32 = 0x2;
pub const OPTION_CLOAK: i32 = 0x4;
pub const OPTION_INVISIBLE: i32 = 0x40;
pub const OPTION_CHASEWALK: i32 = 0x4000;

/// e_mode (mob.hpp): the bits unit code tests.
pub const MD_CANMOVE: u32 = 0x1;
pub const MD_CANATTACK: u32 = 0x80;
pub const MD_MASK: u32 = 0xFFFF;
pub const MD_ANGRY: u32 = 0x800;
pub const MD_TARGETWEAK: u32 = 0x4000;
/// status.cpp status_calc_pc: players get every mode bit but angry / target-weak (and the ones above MD_MASK).
pub const PC_MODE: u32 = MD_MASK & !(MD_ANGRY | MD_TARGETWEAK);

/// mmo.hpp MAX_WALK_SPEED / unit.cpp MOVE_REFRESH_TIME.
pub const MAX_WALK_SPEED: i64 = 1000;
pub const MOVE_REFRESH_TIME: i64 = MAX_WALK_SPEED;
/// clif.hpp CLIF_WALK_TIMER: "walking" for the packets sent while a step is processed.
pub const CLIF_WALK_TIMER: u64 = u64::MAX;

/// e_unit_stop_walking (unit.hpp).
pub const USW_NONE: i32 = 0x0;
pub const USW_FIXPOS: i32 = 0x1;
pub const USW_MOVE_ONCE: i32 = 0x2;
pub const USW_MOVE_FULL_CELL: i32 = 0x4;
pub const USW_FORCE_STOP: i32 = 0x8;
pub const USW_RELEASE_TARGET: i32 = 0x10;

/// clr_type (clif.hpp).
pub const CLR_OUTSIGHT: u8 = 0;
pub const CLR_DEAD: u8 = 1;
pub const CLR_RESPAWN: u8 = 2;
pub const CLR_TELEPORT: u8 = 3;
pub const CLR_TRICKDEAD: u8 = 4;

/// view_data (unit.hpp): look[LOOK_*], sex, dead_sit (0 standing, 1 dead, 2 sitting).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct ViewData {
    pub class: i32,
    pub hair: i32,
    pub weapon: i32,
    pub head_bottom: i32,
    pub head_top: i32,
    pub head_mid: i32,
    pub hair_color: i32,
    pub clothes_color: i32,
    pub shield: i32,
    pub shoes: i32,
    pub body: i32,
    pub robe: i32,
    pub body2: i32,
    pub sex: u8,
    pub dead_sit: u8,
}

/// status_change's display fields (opt1 body state, opt2 health state, opt3, option).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct ScView {
    pub opt1: i16,
    pub opt2: i16,
    pub opt3: i32,
    pub option: i32,
}

/// unit_data (unit.hpp). Timer fields hold timer ids (0 = INVALID_TIMER).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct UnitData {
    pub dir: u8,
    pub walktimer: u64,
    /// walkpath_data: directions, path_len (can be shortened below path.len()), path_pos.
    pub path: Vec<u8>,
    pub path_len: u8,
    pub path_pos: u8,
    pub to_x: i16,
    pub to_y: i16,
    pub sx: u8,
    pub sy: u8,
    pub change_walk_target: bool,
    pub skillcastcancel: bool,
    pub attack_continue: bool,
    pub step_attack: bool,
    pub walk_easy: bool,
    pub running: bool,
    pub walk_script: bool,
    pub blockedmove: bool,
    pub blockedskill: bool,
    pub ignore_cell_stack_limit: bool,
    pub force_walk: bool,
    pub target: u32,
    pub target_to: u32,
    pub chaserange: i32,
    pub attacktimer: u64,
    pub skilltimer: u64,
    pub steptimer: u64,
    pub stepaction: bool,
    pub stepskill_id: u16,
    pub stepskill_lv: u16,
    pub skill_id: u16,
    pub skill_lv: u16,
    pub skilltarget: u32,
    pub skillx: i16,
    pub skilly: i16,
    pub attackabletime: Tick,
    pub canact_tick: Tick,
    pub canmove_tick: Tick,
    pub endure_tick: Tick,
    pub dmg_tick: Tick,
    pub immune_attack: bool,
    pub target_count: u8,
    pub walk_done_event: String,
    pub title: String,
    pub group_id: i32,
}

#[table(accessor = unit,
    index(accessor = by_map_kind, btree(columns = [map, kind])),
    index(accessor = by_map_kind_x, btree(columns = [map, kind, x])))]
#[derive(Clone, Debug, PartialEq)]
pub struct Unit {
    /// block_list id: account id for players, rAthena's id ranges for the rest.
    #[primary_key]
    pub id: u32,
    /// BL_* (map.rs).
    pub kind: u16,
    pub map: u16,
    pub x: i16,
    pub y: i16,
    /// In the map's block list (rAthena `bl->prev != nullptr`): visible to area searches.
    pub on_map: bool,
    pub name: String,
    pub vd: ViewData,
    pub ud: UnitData,
    pub sc: ScView,
    /// status_get_speed.
    pub speed: u16,
    /// status_data mode (e_mode).
    pub mode: u32,
    /// status_get_lv.
    pub level: i32,
    /// Players: char id (the unit packets' GID / "CCODE"), head direction, font, manner, karma.
    pub char_id: u32,
    pub head_dir: u8,
    pub font: i16,
    pub manner: i16,
    pub karma: bool,
    pub guild_id: u32,
    pub emblem_id: i32,
    pub hp: i64,
    pub max_hp: i64,
    pub dead: bool,
    /// SZ_SMALL / SZ_MEDIUM / SZ_BIG (tiny / big units).
    pub size: u8,
    /// Monsters: e_mob_bosstype for the unit packets.
    pub boss: u8,
    /// NPCs: `nd->is_invisible` (disabled NPC) and a dynamic NPC's owner.
    pub invisible: bool,
    pub npc_owner_char: u32,
}

pub fn get(c: &Ctx, id: u32) -> Option<Unit> {
    crate::prof::count("unit_get", 1);
    // script.cpp's dummy_sd, while a script without a player runs an @command as it: a character no table holds
    // (script/dummy_sd.rs). No block has id 0 otherwise.
    if id == crate::script::dummy_sd::ID {
        if let Some(u) = crate::script::dummy_sd::unit() {
            return Some(u);
        }
    }
    if let Some(cached) = MOB_UNIT_CACHE.with(|m| m.borrow().get(&id).cloned()) {
        return Some(cached);
    }
    let row = c.db().unit().id().find(id);
    if let Some(ref u) = row {
        if u.kind == BL_MOB {
            MOB_UNIT_CACHE.with(|m| {
                m.borrow_mut().insert(id, u.clone());
            });
        }
    }
    row
}

thread_local! {
    /// Units freed during this call (unit_free), as rAthena's map_freeblock keeps a block_list until
    /// map_freeblock_unlock. Emptied when a call starts (world::run).
    static FREED: std::cell::RefCell<Vec<Unit>> = const { std::cell::RefCell::new(Vec::new()) };
    /// Per-call write-through cache of `BL_MOB` `Unit` rows, emptied by `freed_reset` (`world::run`). Every monster
    /// `Unit` insert, update and delete goes through `insert_mob`, `save` and `delete_mob`.
    static MOB_UNIT_CACHE: std::cell::RefCell<std::collections::HashMap<u32, Unit>> =
        std::cell::RefCell::new(std::collections::HashMap::new());
    /// Incremented whenever any unit's map position / block order or monster presence changes during a reducer call.
    static MOB_POS_GEN: std::cell::Cell<u64> = const { std::cell::Cell::new(0) };
}

pub fn mob_pos_gen() -> u64 {
    MOB_POS_GEN.with(|g| g.get())
}

pub fn bump_mob_pos_gen() {
    MOB_POS_GEN.with(|g| g.set(g.get().wrapping_add(1)));
}

pub fn cache_mob(u: &Unit) {
    if u.kind == BL_MOB {
        MOB_UNIT_CACHE.with(|m| {
            m.borrow_mut().insert(u.id, u.clone());
        });
    }
}

pub fn insert_mob(c: &Ctx, u: Unit) {
    use spacetimedb::Table;
    MOB_UNIT_CACHE.with(|m| {
        m.borrow_mut().insert(u.id, u.clone());
    });
    bump_mob_pos_gen();
    c.db().unit().insert(u);
}

pub fn delete_mob(c: &Ctx, id: u32) {
    MOB_UNIT_CACHE.with(|m| {
        m.borrow_mut().remove(&id);
    });
    bump_mob_pos_gen();
    c.db().unit().id().delete(id);
}

/// unit_free's map_freeblock: the unit's last state stays readable through `get_or_freed` for this call.
pub fn freed_keep(u: Unit) {
    FREED.with(|f| {
        let mut f = f.borrow_mut();
        f.retain(|x| x.id != u.id);
        f.push(u);
    });
}

/// Forgets the units freed by an earlier call (world::run), and that a command ran as script.cpp's dummy character
/// (script/dummy_sd.rs: it exists only inside one atcommand_sub).
pub fn freed_reset() {
    FREED.with(|f| f.borrow_mut().clear());
    MOB_UNIT_CACHE.with(|m| m.borrow_mut().clear());
    map::block_seq_reset();
    crate::script::dummy_sd::reset();
}

/// A block_list rAthena still holds a pointer to: the live row, else its state when unit_free freed it in this call
/// (map_id2bl would not find it: only code that kept the pointer reads it).
pub fn get_or_freed(c: &Ctx, id: u32) -> Option<Unit> {
    get(c, id).or_else(|| FREED.with(|f| f.borrow().iter().rev().find(|u| u.id == id).cloned()))
}

pub fn save(c: &Ctx, u: &Unit) {
    if u.id == crate::script::dummy_sd::ID && crate::script::dummy_sd::save_unit(u) {
        return; // script.cpp's dummy_sd has no row (script/dummy_sd.rs)
    }
    if u.kind == BL_MOB {
        let same = MOB_UNIT_CACHE.with(|m| {
            let mut m = m.borrow_mut();
            if let Some(old) = m.get(&u.id) {
                if old == u {
                    return true;
                }
                if old.map != u.map || old.x != u.x || old.y != u.y || old.on_map != u.on_map {
                    bump_mob_pos_gen();
                }
            } else {
                bump_mob_pos_gen();
            }
            m.insert(u.id, u.clone());
            false
        });
        if same {
            return;
        }
    } else {
        bump_mob_pos_gen();
    }
    c.db().unit().id().update(u.clone());
}

const OOC_RAMP_DURATION_MS: i32 = 20_000;
const OOC_COMBAT_COOLDOWN_MS: Tick = 5000;

fn ooc_get_accum_ms(u: &Unit) -> i32 {
    (u.ud.group_id & 0xFFFF).clamp(0, OOC_RAMP_DURATION_MS)
}

fn ooc_set_accum_ms(u: &mut Unit, accum_ms: i32) {
    let ms = accum_ms.clamp(0, OOC_RAMP_DURATION_MS) & 0xFFFF;
    u.ud.group_id = if ms <= 0 { 0 } else { ms };
}

/// True when a player is out of combat and away from aggressive/hostile monsters, allowing travel walk speed to ramp.
pub fn pc_out_of_combat(c: &Ctx, u: &Unit, tick: Tick) -> bool {
    if u.kind != BL_PC || u.dead || !u.on_map {
        return false;
    }
    if u.ud.target != 0 || u.ud.target_to != 0 || u.ud.attacktimer != 0 || u.ud.skilltimer != 0 || u.ud.target_count > 0 {
        return false;
    }
    if u.ud.dmg_tick > 0 && tick - u.ud.dmg_tick < OOC_COMBAT_COOLDOWN_MS {
        return false;
    }
    if u.ud.attackabletime > 0 && tick < u.ud.attackabletime {
        return false;
    }
    if let Some(p) = crate::pc::get(c, u.id) {
        if p.canlog_tick > 0 && tick - p.canlog_tick < OOC_COMBAT_COOLDOWN_MS {
            return false;
        }
    }
    !map::units_in_range(c, u.map, u.x, u.y, 14, BL_MOB).iter().any(|m| {
        if m.dead {
            return false;
        }
        if m.ud.target == u.id || m.ud.target_to == u.id {
            return true;
        }
        if crate::mob::get(c, m.id).map(|md| md.target_id == u.id || md.attacked_id == u.id).unwrap_or(false) {
            return true;
        }
        let dist = (m.x - u.x).abs().max((m.y - u.y).abs());
        dist <= 8 && (m.mode & crate::status::defs::MD_AGGRESSIVE as u32) != 0
    })
}

/// Ramps walk delay from `base` down to `base * 2 / 3` (+50% movement speed) over `OOC_RAMP_DURATION_MS` (20s).
fn pc_ramped_speed(base: u16, accum_ms: i32) -> u16 {
    if accum_ms <= 0 {
        return base;
    }
    let s = accum_ms.min(OOC_RAMP_DURATION_MS) as u32;
    let span = OOC_RAMP_DURATION_MS as u32;
    let drop = (base as u32 / 3) * s / span;
    ((base as u32).saturating_sub(drop) as u16).max(crate::status::defs::MIN_WALK_SPEED as u16)
}

pub fn pc_effective_speed(base: u16, u: &Unit) -> u16 {
    let base = base.max(crate::status::defs::MIN_WALK_SPEED as u16);
    if u.kind != BL_PC {
        return base;
    }
    pc_ramped_speed(base, ooc_get_accum_ms(u))
}

fn send_pc_speed(c: &Ctx, u: &Unit) {
    use crate::net::packets::PACKET_ZC_PAR_CHANGE;
    let p = PACKET_ZC_PAR_CHANGE {
        PacketType: PACKET_ZC_PAR_CHANGE::ID as i16,
        varID: crate::status::consts::SP_SPEED as u16,
        count: u.speed as i32,
    };
    clif::send(c, &p.to_vec(), u, clif::Target::SelfOnly);
}

pub fn sync_pc_walk_speed(c: &Ctx, u: &mut Unit, tick: Tick) {
    if u.kind != BL_PC {
        return;
    }
    let Some(sd) = crate::status::data::sd_get(c, u.id) else { return };
    let base = sd.battle_status.speed.max(crate::status::defs::MIN_WALK_SPEED as u16);
    if !pc_out_of_combat(c, u, tick) {
        u.ud.group_id = 0;
    }
    let want = pc_ramped_speed(base, ooc_get_accum_ms(u));
    if u.speed != want {
        u.speed = want;
        send_pc_speed(c, u);
    }
}

pub fn reset_pc_walk_speed(c: &Ctx, u: &mut Unit) {
    if u.kind != BL_PC {
        return;
    }
    u.ud.group_id = 0;
    if let Some(sd) = crate::status::data::sd_get(c, u.id) {
        let base = sd.battle_status.speed.max(crate::status::defs::MIN_WALK_SPEED as u16);
        if u.speed != base {
            u.speed = base;
            send_pc_speed(c, u);
        }
    }
}

pub fn mark_pc_combat(c: &Ctx, u: &mut Unit, tick: Tick) {
    if u.kind != BL_PC {
        return;
    }
    if let Some(mut p) = crate::pc::get(c, u.id) {
        p.canlog_tick = tick;
        crate::pc::put(c, &p);
    }
    reset_pc_walk_speed(c, u);
}

/// status_get_speed.
pub fn speed(u: &Unit) -> i64 {
    u.speed.max(1) as i64
}

/// unit.cpp unit_setdir.
pub fn setdir(c: &Ctx, u: &mut Unit, dir: u8, send_update: bool) {
    u.ud.dir = dir;
    if u.kind == BL_PC {
        u.head_dir = path::DIR_NORTH;
        crate::pc::set_body_direction(c, u.id, dir);
    }
    if send_update {
        clif::changed_dir(c, u, clif::Target::Area);
    }
}

/// unit.cpp unit_set_target.
pub fn set_target(c: &Ctx, u: &mut Unit, target_id: u32) {
    let tick = c.tick();
    if u.ud.target != target_id {
        // (a unit that targets itself counts too: `u` is that unit's row, changed in place)
        if u.ud.target == u.id {
            u.ud.target_count = u.ud.target_count.saturating_sub(1);
        } else if u.ud.target != 0 {
            if let Some(mut t) = get(c, u.ud.target) {
                if t.ud.target_count > 0 {
                    t.ud.target_count -= 1;
                    save(c, &t);
                }
            }
        }
        if target_id == u.id {
            if u.ud.target_count < 255 {
                u.ud.target_count += 1;
            }
        } else if target_id != 0 {
            if let Some(mut t) = get(c, target_id) {
                if t.ud.target_count < 255 {
                    t.ud.target_count += 1;
                }
                if t.kind == BL_PC {
                    mark_pc_combat(c, &mut t, tick);
                }
                save(c, &t);
            }
        }
    }
    u.ud.target = target_id;
    if target_id != 0 && u.kind == BL_PC {
        mark_pc_combat(c, u, tick);
    }
}

/// unit.cpp unit_changetarget (a map_foreach* callback: bl, { src, target }): a unit that attacks or chases `src`
/// (ud->target / ud->target_to) switches to `target` (unit_changetarget_sub). Returns 1 when nothing changed, 0 when
/// the target changed. `bl` is re-read (an earlier callback of the same walk may have changed it).
pub fn changetarget(c: &Ctx, bl: &Unit, src: &Unit, target: &Unit) -> i32 {
    let Some(mut u) = get(c, bl.id) else { return 1 };
    if u.ud.target == target.id {
        return 1;
    }
    if u.ud.target == 0 && u.ud.target_to == 0 {
        return 1;
    }
    if u.ud.target != src.id && u.ud.target_to != src.id {
        return 1;
    }
    changetarget_sub(c, &mut u, target);
    save(c, &u);
    // unit_attack(bl, target->id, ud->state.attack_continue); (commented out in rAthena)
    0
}

/// unit.cpp unit_changetarget_sub: `ud` (the unit `u`, changed in place: the caller saves it) targets `target`
/// unless it is dead: a monster's target_id, ud->target_to and ud->skilltarget when set, unit_set_target.
pub fn changetarget_sub(c: &Ctx, u: &mut Unit, target: &Unit) {
    if crate::status::sc::status_isdead(c, target) {
        return;
    }
    if u.kind == BL_MOB {
        if let Some(mut md) = crate::mob::get(c, u.id) {
            md.target_id = target.id;
            crate::mob::put(c, &md);
        }
    }
    if u.ud.target_to > 0 {
        u.ud.target_to = target.id;
    }
    if u.ud.skilltarget > 0 {
        u.ud.skilltarget = target.id;
    }
    set_target(c, u, target.id);
}

/// unit.cpp unit_stop_attack.
pub fn stop_attack(c: &Ctx, u: &mut Unit) {
    set_target(c, u, 0);
    if u.ud.attacktimer == 0 {
        return;
    }
    timer::delete(c, u.ud.attacktimer);
    u.ud.attacktimer = 0;
}

/// unit.cpp unit_unattackable (monster / pet target release comes with phase 3).
pub fn unattackable(c: &Ctx, u: &mut Unit) {
    u.ud.attack_continue = false;
    u.ud.step_attack = false;
    u.ud.target_to = 0;
    set_target(c, u, 0);
}

/// unit.cpp unit_stop_stepaction.
pub fn stop_stepaction(c: &Ctx, u: &mut Unit) {
    u.ud.stepaction = false;
    u.ud.target_to = 0;
    u.ud.stepskill_id = 0;
    u.ud.stepskill_lv = 0;
    if u.ud.steptimer != 0 {
        timer::delete(c, u.ud.steptimer);
        u.ud.steptimer = 0;
    }
}

/// unit.cpp unit_can_move.
pub fn can_move(c: &Ctx, u: &Unit) -> bool {
    use crate::db::skills::{skill_get_inf2, INF2_ISGUILD, LG_EXEEDBREAK, SA_FREECAST};
    if u.dead {
        return false;
    }
    if u.ud.skilltimer != 0
        && u.ud.skill_id != LG_EXEEDBREAK
        && (u.kind != BL_PC || crate::skill::checkskill(c, u.id, SA_FREECAST) == 0 || skill_get_inf2(u.ud.skill_id, &[INF2_ISGUILD]))
    {
        return false; // Prevent moving while casting
    }
    if u.ud.canmove_tick - c.tick() > 0 {
        return false;
    }
    if u.kind == BL_PC && crate::pc::cant_move(c, u) {
        return false;
    }
    if u.ud.blockedmove {
        return false;
    }
    // Status changes that block movement (sc->cant.move, status_calc_state)
    if crate::status::sc::cant(c, u.id, crate::status::consts::SCS_NOMOVE) {
        return false;
    }
    // Icewall walk block special trapped monster mode
    if u.kind == BL_MOB {
        use crate::mob::conf::{boss_icewall_walk_block, mob_icewall_walk_block};
        let immune = u.mode & (crate::status::defs::MD_STATUSIMMUNE as u32) == (crate::status::defs::MD_STATUSIMMUNE as u32);
        if ((immune && boss_icewall_walk_block() == 1) || (!immune && mob_icewall_walk_block() == 1))
            && crate::map_cell::getcell(c, Some(u.map), u.x, u.y, crate::map_cell::CELL_CHKICEWALL) != 0
        {
            if let Some(mut md) = crate::mob::get(c, u.id) {
                md.walktoxy_fail_count = 1; // Make sure rudeattacked skills are invoked
                crate::mob::put(c, &md);
                return false;
            }
        }
    }
    true
}

fn walk_speed(u: &Unit, dir: u8) -> i64 {
    if path::direction_diagonal(dir) {
        speed(u) * MOVE_DIAGONAL_COST as i64 / MOVE_COST as i64
    } else {
        speed(u)
    }
}

/// unit.cpp unit_walktoxy_nextcell: arms the timer for the next step; `false` when the path is done.
pub fn walktoxy_nextcell(c: &Ctx, u: &mut Unit, send_move: bool, tick: Tick) -> bool {
    if u.ud.path_pos >= u.ud.path_len {
        // The reply is needed even at the target cell so the client resynchronises its position.
        if send_move && u.kind == BL_PC {
            clif::walkok(c, u);
        }
        return false;
    }
    // Monsters first check for a chase skill and if they didn't use one if their target is in range each cell after
    // checking for a chase skill (unit.cpp:195-224: the monster system's part)
    let mut send_move = send_move;
    if u.kind == BL_MOB {
        save(c, u);
        let r = crate::mob::unit::nextcell_mob(c, u.id, &mut send_move, tick);
        if let Some(fresh) = get(c, u.id) {
            *u = fresh;
        }
        if let Some(r) = r {
            return r;
        }
        if u.ud.path_pos >= u.ud.path_len {
            return false;
        }
    }
    let speed = walk_speed(u, u.ud.path[u.ud.path_pos as usize]);
    if u.ud.walktimer != 0 && u.ud.walktimer != CLIF_WALK_TIMER {
        timer::delete(c, u.ud.walktimer);
        u.ud.walktimer = 0;
    }
    u.ud.walktimer = timer::add(c, tick + speed, Func::UnitWalk, u.id, speed);
    if send_move || tick - u.ud.dmg_tick < MOVE_REFRESH_TIME {
        clif::r#move(c, u);
        if u.kind == BL_PC {
            clif::walkok(c, u);
        }
    }
    true
}

/// unit.cpp unit_walktoxy_sub: finds the path to (to_x, to_y) and starts walking. 1 = walking.
pub fn walktoxy_sub(c: &Ctx, u: &mut Unit) -> bool {
    let Some(wpd) = path::path_search(u.map, u.x, u.y, u.ud.to_x, u.ud.to_y, u.ud.walk_easy as i32, CellChk::ChkNoPass) else {
        return false;
    };
    // OFFICIAL_WALKPATH (config/core.hpp): more than 14 cells only in a straight, unobstructed line.
    if u.kind != BL_NPC && wpd.path.len() > 14 && !path::path_search_long(u.map, u.x, u.y, u.ud.to_x, u.ud.to_y, CellChk::ChkNoPass) {
        return false;
    }
    u.ud.path_len = wpd.path.len() as u8;
    u.ud.path = wpd.path;
    u.ud.path_pos = 0;
    // Monsters always target an adjacent tile even when ranged; others shorten the path by their chase range.
    if u.ud.target_to != 0 && u.ud.chaserange > 1 && u.kind != BL_MOB {
        let mut i = u.ud.chaserange * 10 - 10;
        while i > 0 && u.ud.path_len > 1 {
            u.ud.path_len -= 1;
            let dir = u.ud.path[u.ud.path_len as usize];
            if path::direction_diagonal(dir) {
                i -= MOVE_COST * 2;
            } else {
                i -= MOVE_COST;
            }
            u.ud.to_x -= DIRX[dir as usize];
            u.ud.to_y -= DIRY[dir as usize];
        }
    }
    u.ud.change_walk_target = false;
    if u.kind == BL_PC {
        u.head_dir = path::DIR_NORTH;
    } else if u.kind == BL_NPC {
        // PACKETVER >= 20170726: a walking NPC with a player sprite is re-spawned as a player unit (unit_npc.rs)
        crate::unit_npc::walktoxy_sub_start(c, u);
    } else if u.kind == BL_MOB && u.ud.attack_continue {
        // Set mobstate here already as chase skills can be used on the first frame of movement
        crate::mob::unit::walktoxy_sub_mob(c, u);
    }
    let tick = c.tick();
    if u.kind == BL_PC {
        sync_pc_walk_speed(c, u, tick);
    }
    walktoxy_nextcell(c, u, true, tick);
    true
}

/// unit.cpp unit_walktobl at the path other systems call (server/ARCHITECTURE.md); the port is in src/mob/unit.rs.
pub fn walktobl(c: &Ctx, id: u32, tbl: &Unit, range: i32, flag: u8) -> bool {
    crate::mob::unit::unit_walktobl(c, id, tbl.id, range, flag as i32)
}

/// unit.cpp unit_walktoxy. flag: &1 easy path, &2 ignore can-move, &4 delay while a walk delay runs, &8 find a free
/// cell near the target first. The unit is saved.
pub fn walktoxy(c: &Ctx, id: u32, x: i16, y: i16, flag: i32) -> bool {
    let Some(mut u) = get(c, id) else { return false };
    let (mut x, mut y) = (x, y);
    if flag & 8 != 0 {
        match map::nearby_freecell(c, u.map, x, y, map::BL_CHAR | BL_NPC, 1) {
            Some((fx, fy)) => {
                x = fx;
                y = fy;
            }
            None => return false,
        }
    }
    let Some(wpd) = path::path_search(u.map, u.x, u.y, x, y, flag & 1, CellChk::ChkNoPass) else {
        log::debug!("walktoxy {id}: no path ({},{}) -> ({x},{y})", u.x, u.y);
        return false;
    };
    if u.kind != BL_NPC {
        if wpd.path.len() as i32 > crate::conf::max_walk_path() {
            log::debug!("walktoxy {id}: path of {} cells from ({},{}) to ({x},{y}) is over max_walk_path", wpd.path.len(), u.x, u.y);
            return false;
        }
        if wpd.path.len() > 14 && !path::path_search_long(u.map, u.x, u.y, x, y, CellChk::ChkNoPass) {
            log::debug!("walktoxy {id}: {} cells from ({},{}) to ({x},{y}) and no straight line", wpd.path.len(), u.x, u.y);
            return false;
        }
    }
    if flag & 4 != 0 {
        unattackable(c, &mut u);
        stop_attack(c, &mut u);
        let wait = u.ud.canmove_tick - c.tick();
        if wait > 0 && wait < 2000 {
            timer::add(c, u.ud.canmove_tick + 1, Func::UnitDelayWalk, u.id, ((x as i64) << 16) | (y as i64 & 0xffff));
            save(c, &u);
            return true;
        }
    }
    if flag & 2 == 0 && (u.mode & MD_CANMOVE != MD_CANMOVE || !can_move(c, &u)) {
        save(c, &u);
        return false;
    }
    u.ud.walk_easy = flag & 1 != 0;
    u.ud.to_x = x;
    u.ud.to_y = y;
    stop_attack(c, &mut u);
    // SC_CONFUSION randomises the target (status changes arrive in phase 4).
    if u.ud.walktimer != 0 {
        // Changing the destination mid-step: the step timer picks the new path up at the next cell centre.
        log::debug!("walktoxy {id}: walking (timer {}), new target ({x},{y}) at the next cell", u.ud.walktimer);
        u.ud.change_walk_target = true;
        save(c, &u);
        return true;
    }
    // Start timer to recall summon (unit.cpp:903-913): sd->md, sd->ed (companion/unit.rs), sd->hd
    // (homunculus/unit.rs), sd->pd (pet/ai.rs)
    if u.kind == BL_PC {
        crate::companion::unit::master_walks(c, id);
        crate::homunculus::unit::unit_check_start_teleport_timer(c, id);
        crate::pet::ai::unit_check_start_teleport_timer(c, id);
    }
    let ok = walktoxy_sub(c, &mut u);
    save(c, &u);
    ok
}

/// unit.cpp unit_delay_walktoxy_timer.
pub fn delay_walktoxy_timer(c: &Ctx, _tick: Tick, id: u32, data: i64) {
    let Some(u) = get(c, id) else { return };
    if !u.on_map || u.dead {
        return;
    }
    walktoxy(c, id, ((data >> 16) & 0xffff) as i16, (data & 0xffff) as i16, 0);
}

/// unit.cpp unit_walktoxy_timer: one step along the path.
pub fn walktoxy_timer(c: &Ctx, tid: u64, tick: Tick, id: u32, _data: i64) {
    let Some(mut u) = get(c, id) else { return };
    if u.ud.walktimer != tid {
        log::error!("unit_walk_timer mismatch {} != {}", u.ud.walktimer, tid);
        return;
    }
    u.ud.walktimer = 0;
    u.ud.sx = 8;
    u.ud.sy = 8;
    if !u.on_map || u.dead || u.ud.path_pos >= u.ud.path_len {
        save(c, &u);
        return;
    }
    let Some(&dir) = u.ud.path.get(u.ud.path_pos as usize) else {
        save(c, &u);
        return;
    };
    if dir >= path::DIR_MAX {
        save(c, &u);
        return;
    };
    setdir(c, &mut u, dir, false);
    let (dx, dy) = (DIRX[dir as usize], DIRY[dir as usize]);
    let (x, y) = (u.x, u.y);
    // Get icewall walk block depending on Status Immune mode (players can't be trapped)
    let md = if u.kind == BL_MOB { crate::mob::get(c, u.id) } else { None };
    let icewall_walk_block: u8 = match &md {
        Some(md) if crate::mob::has_mode(md, crate::status::defs::MD_STATUSIMMUNE) => crate::mob::conf::boss_icewall_walk_block(),
        Some(_) => crate::mob::conf::mob_icewall_walk_block(),
        None => 0,
    };
    let m = u.map;
    let icewall = |x: i16, y: i16| crate::map_cell::getcell(c, Some(m), x, y, crate::map_cell::CELL_CHKICEWALL) != 0;
    // Monsters will walk into an icewall from the west and south if they already started walking
    if map::getcell(c, u.map, x + dx, y + dy, CellChk::ChkNoPass) && (icewall_walk_block == 0 || dx < 0 || dy < 0 || !icewall(x + dx, y + dy)) {
        walktoxy_sub(c, &mut u);
        save(c, &u);
        return;
    }
    // Monsters can only leave icewalls to the west and south
    // But if movement fails more than icewall_walk_block times, they can ignore this rule
    if let Some(mut md) = md {
        if !u.ud.force_walk && md.walktoxy_fail_count < icewall_walk_block && icewall(x, y) && (dx > 0 || dy > 0) {
            // Needs to be done here so that rudeattack skills are invoked
            md.walktoxy_fail_count += 1;
            crate::mob::put(c, &md);
            save(c, &u);
            clif::fixpos(c, &u);
            // Monsters in this situation will unlock target
            crate::mob::ai::mob_unlocktarget(c, u.id, tick);
            return;
        }
    }
    let kinds = if u.kind == BL_PC { map::BL_ALL } else { BL_PC };
    // Refresh the view for everything this step takes out of sight…
    for other in map::units_in_move_area(c, &u, crate::conf::area_size(), dx, dy, kinds) {
        clif::outsight(c, &other, &u);
    }
    let (nx, ny) = (x + dx, y + dy);
    moveblock(c, &mut u, nx, ny, tick);
    if u.x != nx || u.y != ny || u.ud.walktimer != 0 {
        return; // map_moveblock has altered the object beyond what we expected (moved/warped it) (unit.cpp:627)
    }
    // …and everything it brings into sight (sent with walking packets).
    let enter = map::units_in_move_area(c, &u, crate::conf::area_size(), -dx, -dy, kinds);
    if !enter.is_empty() {
        u.ud.walktimer = CLIF_WALK_TIMER;
        save(c, &u);
        for other in enter {
            clif::insight(c, &other, &u);
        }
        u.ud.walktimer = 0;
    }
    save(c, &u);
    let at_destination = u.x == u.ud.to_x && u.y == u.ud.to_y;
    if at_destination {
        // player-sprite NPCs turn back into NPC units, monsters' escape state ends, force_walk ends,
        // walk_done_event runs (unit_npc.rs)
        if !crate::unit_npc::walk_arrived(c, id) {
            return;
        }
        match get(c, id) {
            Some(u2) => u = u2,
            None => return,
        }
    }
    if u.kind == BL_MOB {
        // escape state, walktoxy_fail_count, npc_touch_areanpc2 (the monster system's part)
        if crate::mob::unit::after_step_mob(c, id, at_destination) {
            return; // Warped
        }
        match get(c, id) {
            Some(u2) if u2.on_map => u = u2,
            _ => return,
        }
    }
    if u.kind == BL_NPC {
        // an NPC with an OnTouch area touches the players it walks onto (unit_walktoxy_ontouch)
        crate::unit_npc::walk_step_npc(c, &u);
        match get(c, id) {
            Some(u2) => u = u2,
            None => return,
        }
    }
    if u.kind == BL_PC {
        // npc_touchnext_areanpc / npc_touch_area_allnpc: warps and OnTouch areas.
        if map::getcell(c, u.map, nx, ny, CellChk::ChkNpc) {
            crate::npc::touch_area_allnpc(c, u.id, u.map, nx, ny);
            match get(c, id) {
                Some(u2) if u2.on_map && u2.map == u.map => u = u2,
                _ => return, // a warp moved the player: this walk is over
            }
        } else {
            crate::pc::clear_areanpc(c, u.id);
        }
        if pc_out_of_combat(c, &u, tick) {
            let step_ms = walk_speed(&u, dir).clamp(50, 500) as i32;
            let next_ms = (ooc_get_accum_ms(&u) + step_ms).min(OOC_RAMP_DURATION_MS);
            ooc_set_accum_ms(&mut u, next_ms);
            if let Some(sd) = crate::status::data::sd_get(c, u.id) {
                let base = sd.battle_status.speed.max(crate::status::defs::MIN_WALK_SPEED as u16);
                let want = pc_ramped_speed(base, next_ms);
                let full = pc_ramped_speed(base, OOC_RAMP_DURATION_MS);
                if (u.speed as i32 - want as i32).abs() >= 4 || (want == full && u.speed != want) {
                    u.speed = want;
                    send_pc_speed(c, &u);
                }
            }
        } else {
            reset_pc_walk_speed(c, &mut u);
        }
    }
    // If stepaction is set then we remembered a client request that should be executed on the next step
    crate::battle::unit_attack::walktoxy_arm_steptimer(c, &mut u, tick);
    if u.ud.change_walk_target {
        if walktoxy_sub(c, &mut u) {
            save(c, &u);
        } else {
            clif::fixpos(c, &u);
            save(c, &u);
        }
        return;
    }
    u.ud.path_pos += 1;
    if walktoxy_nextcell(c, &mut u, false, tick) {
        save(c, &u);
        return;
    }
    if u.ud.running {
        // Keep trying to run. (unit.cpp:751-753: SC_RUN, else SC_WUGDASH with the player)
        save(c, &u);
        let is_pc = u.kind == BL_PC;
        if !(crate::status::sc_ext::unit_run(c, id, false, crate::status::consts::SC_RUN) || crate::status::sc_ext::unit_run(c, id, is_pc, crate::status::consts::SC_WUGDASH)) {
            if let Some(mut u2) = get(c, id) {
                u2.ud.running = false;
                save(c, &u2);
            }
        }
        return;
    }
    if !u.ud.stepaction && u.ud.target_to > 0 {
        // Update target trajectory (unit_update_chase: the monster system's port).
        save(c, &u);
        if crate::mob::unit::chase_after_path(c, id, tick) {
            return;
        }
        u = match get(c, id) {
            Some(u2) => u2,
            None => return,
        };
    }
    // Stopped walking.
    if u.kind == BL_PC && !pc_out_of_combat(c, &u, tick) {
        reset_pc_walk_speed(c, &mut u);
    }
    u.ud.to_x = u.x;
    u.ud.to_y = u.y;
    save(c, &u);
    if u.kind != BL_NPC && !u.ud.ignore_cell_stack_limit && crate::conf::official_cell_stack_limit() > 0
        && map::count_oncell(c, u.map, nx, ny, map::BL_CHAR | BL_NPC, 1) > crate::conf::official_cell_stack_limit() as usize
    {
        // Walked onto an occupied cell: move on to a free one next to it.
        if walktoxy(c, id, nx, ny, 8) {
            // Execute step timer on next step instead
            if let Some(mut u2) = get(c, id) {
                if u2.ud.steptimer != 0 {
                    timer::delete(c, u2.ud.steptimer);
                    u2.ud.steptimer = 0;
                    save(c, &u2);
                }
            }
        }
    }
}

/// map.cpp map_moveblock: new coordinates; a unit that crosses into another block goes to the head of that block's
/// list (map_delblock + map_addblock: map::block_added), the (map, x) index does the rest.
pub fn moveblock(c: &Ctx, u: &mut Unit, x: i16, y: i16, tick: Tick) {
    let (x0, y0) = (u.x, u.y);
    // BL_CHAR: skill_unit_move(bl, tick, 2) on the old cell (skillunit; the unit row is saved first). The status
    // changes that end on moving (SC_CLOSECONFINE …) belong to the status changes (status2).
    let hooks = u.on_map && u.kind & map::BL_CHAR != 0;
    if hooks {
        crate::skillunit::movement::moveblock_before(c, u, tick);
        if let Some(fresh) = get(c, u.id) {
            *u = fresh;
        }
    }
    let moveblock = u.x / map::BLOCK_SIZE != x / map::BLOCK_SIZE || u.y / map::BLOCK_SIZE != y / map::BLOCK_SIZE;
    u.x = x;
    u.y = y;
    if u.on_map && moveblock {
        map::block_added(c, u.id);
    }
    // an NPC's npc_data is its block_list: nd->x / nd->y move with it
    crate::unit_npc::npc_moved(c, u);
    if hooks {
        // skill_unit_move(bl, tick, 3) and the unit groups that follow the character (skillunit)
        save(c, u);
        crate::skillunit::movement::moveblock_after(c, u, x0, y0, tick);
        if let Some(fresh) = get(c, u.id) {
            *u = fresh;
        }
    }
}

/// unit.cpp unit_stop_walking. The unit is updated in place (callers save it).
pub fn stop_walking(c: &Ctx, u: &mut Unit, kind: i32, canmove_delay: Tick) -> bool {
    if kind & USW_RELEASE_TARGET != 0 {
        u.ud.target_to = 0;
    }
    if kind & USW_FORCE_STOP == 0 && u.ud.walktimer == 0 {
        if u.kind == BL_PC && !pc_out_of_combat(c, u, c.tick()) {
            reset_pc_walk_speed(c, u);
        }
        return false;
    }
    let mut td = None;
    if u.ud.walktimer != 0 && u.ud.walktimer != CLIF_WALK_TIMER {
        td = timer::get(c, u.ud.walktimer);
        timer::delete(c, u.ud.walktimer);
    }
    u.ud.walktimer = 0;
    u.ud.change_walk_target = false;
    let tick = c.tick();
    let move_once = kind & USW_MOVE_ONCE != 0 && u.ud.path_pos == 0;
    let move_full = kind & USW_MOVE_FULL_CELL != 0 && td.map(|(due, data)| due - tick <= data / 2).unwrap_or(false);
    if move_once || move_full {
        u.ud.path_len = u.ud.path_pos + 1;
        // unit_walktoxy_timer(INVALID_TIMER, tick, id, path_pos): walktimer is INVALID_TIMER too, so the step runs now.
        save(c, u);
        walktoxy_timer(c, 0, tick, u.id, 0);
        if let Some(fresh) = get(c, u.id) {
            *u = fresh;
        }
    }
    if kind & USW_FIXPOS != 0 {
        u.ud.sx = 8;
        u.ud.sy = 8;
        clif::fixpos(c, u);
    }
    if u.kind == BL_PC && !pc_out_of_combat(c, u, tick) {
        reset_pc_walk_speed(c, u);
    }
    u.ud.path_len = 0;
    u.ud.path_pos = 0;
    u.ud.to_x = u.x;
    u.ud.to_y = u.y;
    if canmove_delay > 0 {
        u.ud.canmove_tick = c.tick() + canmove_delay;
    }
    // Re-added, the check in unit_set_walkdelay means dmg during running won't fall through to this place in code [Kevin]
    // (unit.cpp:1748-1752; the unit is saved first: the status ends read and write it)
    if u.ud.running {
        save(c, u);
        crate::status::sc::status_change_end(c, u.id, crate::status::consts::SC_RUN, 0);
        crate::status::sc::status_change_end(c, u.id, crate::status::consts::SC_WUGDASH, 0);
        if let Some(fresh) = get(c, u.id) {
            *u = fresh;
        }
    }
    true
}

/// unit.cpp unit_movepos: instant move to (x, y) (knock-back, @jump, …).
pub fn movepos(c: &Ctx, id: u32, dst_x: i16, dst_y: i16, easy: i32, checkpath: bool) -> bool {
    let Some(mut u) = get(c, id) else { return false };
    stop_walking(c, &mut u, USW_FIXPOS, 0);
    stop_attack(c, &mut u);
    if checkpath
        && (map::getcell(c, u.map, dst_x, dst_y, CellChk::ChkNoPass)
            || path::path_search(u.map, u.x, u.y, dst_x, dst_y, easy, CellChk::ChkNoReach).is_none())
    {
        save(c, &u);
        return false;
    }
    u.ud.to_x = dst_x;
    u.ud.to_y = dst_y;
    let d = map::calc_dir(&u, dst_x, dst_y);
    setdir(c, &mut u, d, false);
    let (dx, dy) = (dst_x - u.x, dst_y - u.y);
    let kinds = if u.kind == BL_PC { map::BL_ALL } else { BL_PC };
    for other in map::units_in_move_area(c, &u, crate::conf::area_size(), dx, dy, kinds) {
        clif::outsight(c, &other, &u);
    }
    moveblock(c, &mut u, dst_x, dst_y, c.tick());
    u.ud.walktimer = CLIF_WALK_TIMER;
    save(c, &u);
    for other in map::units_in_move_area(c, &u, crate::conf::area_size(), -dx, -dy, kinds) {
        clif::insight(c, &other, &u);
    }
    u.ud.walktimer = 0;
    save(c, &u);
    if u.kind == BL_PC {
        if map::getcell(c, u.map, u.x, u.y, CellChk::ChkNpc) {
            crate::npc::touch_area_allnpc(c, u.id, u.map, u.x, u.y);
            if !get(c, id).map(|u2| u2.on_map).unwrap_or(false) {
                return false;
            }
        } else {
            crate::pc::clear_areanpc(c, u.id);
        }
        // Check if pet needs to be teleported. [Skotlex] (unit.cpp:1220-1236, pet/ai.rs)
        crate::pet::ai::unit_movepos_pet(c, id, checkpath, dst_x, dst_y);
    }
    true
}

/// unit.cpp unit_remove_map: takes the unit off its map (clr_type tells the clients how it vanishes).
/// The unit is saved.
pub fn remove_map(c: &Ctx, id: u32, clrtype: u8) -> bool {
    let Some(mut u) = get(c, id) else { return false };
    if !u.on_map {
        return false;
    }
    if u.kind == BL_MOB {
        // the BL_MOB flavour of unit_remove_map (dead monsters vanish through mob_dead) is the monster system's
        return crate::mob::unit::unit_remove_map(c, id, clrtype);
    }
    if u.kind == map::BL_PET {
        // the BL_PET flavour (a pet without intimacy runs away) is the pet system's (pet/ai.rs)
        return crate::pet::ai::unit_remove_map(c, id, clrtype);
    }
    if u.ud.walktimer != 0 {
        stop_walking(c, &mut u, USW_RELEASE_TARGET, 0);
    }
    if clrtype == CLR_DEAD {
        u.ud.blockedmove = true;
    }
    if u.ud.skilltimer != 0 {
        // unit.cpp:3598 unit_skillcastcancel(bl, 0) (skill/unit.rs)
        save(c, &u);
        crate::skill::unit::unit_skillcastcancel(c, id, 0);
        u = get(c, id).unwrap_or(u);
    }
    if u.ud.target != 0 || u.ud.attacktimer != 0 {
        stop_attack(c, &mut u);
    }
    if u.ud.stepaction || u.ud.steptimer != 0 {
        stop_stepaction(c, &mut u);
    }
    u.ud.attackabletime = c.tick();
    u.ud.canmove_tick = c.tick();
    // map-change/warp dispells (unit.cpp:3612-3624)
    if !crate::status::sc::load(c, id).empty() {
        save(c, &u);
        remove_map_statuses(c, &u);
        if let Some(fresh) = get(c, id) {
            u = fresh;
        }
    }
    if u.kind == BL_PC {
        save(c, &u);
        crate::pc::on_remove_map(c, &mut u);
    }
    if u.kind == map::BL_HOM {
        // unit.cpp:3761-3774 (homunculus/unit.rs): a homunculus without intimacy is freed here
        if !crate::homunculus::unit::remove_map_hom(c, &mut u, clrtype) {
            return false;
        }
    }
    if crate::companion::is_kind(u.kind) {
        // BL_MER / BL_ELEM (unit.cpp:3775-3800): the can-act tick, and a companion without time left is freed
        // (companion/unit.rs)
        if let Some(r) = crate::companion::unit::remove_map_arm(c, &mut u, clrtype) {
            return r;
        }
    }
    // NPC specifics arrive with that system.
    if u.kind & (map::BL_CHAR | map::BL_PET) != 0 {
        // unit.cpp:3811-3814: skill_unit_move(bl, tick, 4) (skillunit), skill_cleartimerskill (skillcore)
        save(c, &u);
        crate::skillunit::skill_unit_move(c, &u, c.tick(), 4);
        if let Some(fresh) = get(c, id) {
            u = fresh;
        }
        crate::skill::timerskill::skill_cleartimerskill(c, &u);
        if let Some(fresh) = get(c, id) {
            u = fresh;
        }
    }
    clif::clearunit_area(c, &u, clrtype);
    u.on_map = false;
    save(c, &u);
    true
}

/// unit_remove_map's status part for a unit with status changes (unit.cpp:3612-3624): SCF_REMOVEONCHANGEMAP ends
/// (removeByStatusFlag), a non-player's cloaking ends (a player's is handled in its own part), a self-cast Gospel and an
/// infinite Provoke end.
fn remove_map_statuses(c: &Ctx, u: &Unit) {
    use crate::status::consts::*;
    use crate::status::sc;
    let has = |t| sc::has_sce(c, u.id, t);
    for t in crate::db::misc::status::status_db().remove_by_status_flag(&has, &[SCF_REMOVEONCHANGEMAP]) {
        sc::status_change_end(c, u.id, t, 0);
    }
    if u.kind != BL_PC {
        sc::status_change_end(c, u.id, SC_CLOAKING, 0);
        sc::status_change_end(c, u.id, SC_CLOAKINGEXCEED, 0);
    }
    if sc::get_sce(c, u.id, SC_GOSPEL).map(|e| e.val4 == crate::status::defs::BCT_SELF).unwrap_or(false) {
        sc::status_change_end(c, u.id, SC_GOSPEL, 0);
    }
    if sc::get_sce(c, u.id, SC_PROVOKE).map(|e| e.val4 == 1).unwrap_or(false) {
        sc::status_change_end(c, u.id, SC_PROVOKE, 0); //End infinite provoke to prevent exploit
    }
}

/// map.cpp map_addblock: puts the unit on its map.
pub fn addblock(c: &Ctx, u: &mut Unit) -> bool {
    let Some(md) = crate::data::maps().get(u.map) else { return false };
    if u.on_map {
        log::error!("map_addblock: unit {} already on a map", u.id);
        return false;
    }
    if u.x < 0 || u.x >= md.xs || u.y < 0 || u.y >= md.ys {
        log::error!("map_addblock: out-of-bounds coordinates (\"{}\",{},{}), map is {}x{}", md.name, u.x, u.y, md.xs, md.ys);
        return false;
    }
    u.on_map = true;
    save(c, u);
    map::block_added(c, u.id); // at the head of its block's list
    true
}

/// unit.cpp unit_getdir.
pub fn getdir(u: &Unit) -> u8 {
    u.ud.dir
}
