//! The skill fields of map_session_data (pc.hpp), one `pc_skill` row per character in the world (created at
//! pc_authok with rAthena's initial values, removed by unit_free), and the pc.cpp functions that own the sphere-like
//! counters: pc_addspiritball / pc_delspiritball (+ pc_spiritball_timer), pc_addsoulball / pc_delsoulball,
//! pc_addservantball / pc_delservantball, pc_addabyssball / pc_delabyssball, pc_addspiritcharm / pc_delspiritcharm
//! (+ pc_spiritcharm_timer), with their packets (clif_spiritball / clif_millenniumshield, clif_soulball,
//! clif_servantball, clif_abyssball, clif_spiritcharm).

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::net::packets::*;
use crate::timer::{self, Func};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// pc.hpp MAX_SPIRITBALL / MAX_DEVOTION / MAX_STELLAR_MARKS / MAX_UNITED_SOULS / MAX_SERVANTBALL / MAX_SERVANT_SIGN;
/// skill.hpp MAX_SKILL_CRIMSON_MARKER; pc.hpp MAX_SPIRITCHARM, MAX_SOUL_BALL, MAX_ABYSSBALL.
pub const MAX_SPIRITBALL: i32 = 15;
pub const MAX_DEVOTION: usize = 5;
pub const MAX_STELLAR_MARKS: usize = 5;
pub const MAX_UNITED_SOULS: usize = 12;
pub const MAX_SERVANTBALL: i32 = 5;
pub const MAX_SERVANT_SIGN: usize = 5;
pub const MAX_SKILL_CRIMSON_MARKER: usize = 3;
pub const MAX_SPIRITCHARM: i32 = 10;
pub const MAX_SOUL_BALL: i32 = 20;
pub const MAX_ABYSSBALL: i32 = 5;

/// pc.hpp spirit_charm_types.
pub const CHARM_TYPE_NONE: i32 = 0;
pub const CHARM_TYPE_WATER: i32 = 1;
pub const CHARM_TYPE_LAND: i32 = 2;
pub const CHARM_TYPE_FIRE: i32 = 3;
pub const CHARM_TYPE_WIND: i32 = 4;

/// map_session_data's skill fields.
#[table(accessor = pc_skill)]
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PcSkill {
    #[primary_key]
    pub account_id: u32,
    /// sd->skillitem, skillitemlv, skillitem_keep_requirement: a skill used through an item (itemskill).
    pub skillitem: i16,
    pub skillitemlv: i16,
    pub skillitem_keep_requirement: bool,
    /// sd->skill_id_old, skill_lv_old.
    pub skill_id_old: u16,
    pub skill_lv_old: u16,
    /// sd->skill_id_dance, skill_lv_dance.
    pub skill_id_dance: u16,
    pub skill_lv_dance: u16,
    /// sd->skill_id_song, skill_lv_song.
    pub skill_id_song: u16,
    pub skill_lv_song: u16,
    /// sd->canequip_tick, canskill_tick (pc_authok: the login tick).
    pub canequip_tick: Tick,
    pub canskill_tick: Tick,
    /// sd->state.autocast (Autospell flag).
    pub autocast: bool,
    /// sd->state.abra_flag (Abracadabra).
    pub abra_flag: u8,
    /// sd->spiritball, spiritball_old, spirit_timer[] (timer ids, 0 = INVALID_TIMER; `spiritball` of them in use).
    pub spiritball: i8,
    pub spiritball_old: i8,
    pub spirit_timer: Vec<u64>,
    /// sd->spiritcharm, spiritcharm_type, spiritcharm_timer[].
    pub spiritcharm: i16,
    pub spiritcharm_type: i32,
    pub spiritcharm_timer: Vec<u64>,
    /// sd->soulball, soulball_old; servantball, servantball_old; abyssball, abyssball_old.
    pub soulball: i8,
    pub soulball_old: i8,
    pub servantball: i8,
    pub servantball_old: i8,
    pub abyssball: i8,
    pub abyssball_old: i8,
    /// sd->devotion[MAX_DEVOTION] (account ids devoted to), stellar_mark[], united_soul[], servant_sign[],
    /// c_marker[MAX_SKILL_CRIMSON_MARKER].
    pub devotion: Vec<u32>,
    pub stellar_mark: Vec<u32>,
    pub united_soul: Vec<u32>,
    pub servant_sign: Vec<u32>,
    pub c_marker: Vec<u32>,
    /// sd->shadowform_id.
    pub shadowform_id: u32,
    /// sd->skill_keep_using { tid, skill_id, level, target }.
    pub keep_using_tid: u64,
    pub keep_using_skill_id: u16,
    pub keep_using_level: u16,
    pub keep_using_target: u32,
    /// sd->scd's iteration order (the keys of the cooldown map in libstdc++'s list order, its bucket count and the
    /// rehash policy's next resize: skill/umap.rs); the cooldowns themselves are `skill_scd` rows.
    pub scd_order: Vec<u16>,
    pub scd_buckets: u64,
    pub scd_next_resize: u64,
}

impl PcSkill {
    /// sd->scd's order state.
    pub fn scd(&self) -> super::umap::Umap {
        super::umap::Umap { order: self.scd_order.clone(), buckets: self.scd_buckets, next_resize: self.scd_next_resize }
    }
    pub fn set_scd(&mut self, m: super::umap::Umap) {
        self.scd_order = m.order;
        self.scd_buckets = m.buckets;
        self.scd_next_resize = m.next_resize;
    }
}

fn fresh(aid: u32) -> PcSkill {
    PcSkill {
        account_id: aid,
        devotion: vec![0; MAX_DEVOTION],
        stellar_mark: vec![0; MAX_STELLAR_MARKS],
        united_soul: vec![0; MAX_UNITED_SOULS],
        servant_sign: vec![0; MAX_SERVANT_SIGN],
        c_marker: vec![0; MAX_SKILL_CRIMSON_MARKER],
        spirit_timer: vec![0; MAX_SPIRITBALL as usize],
        spiritcharm_timer: vec![0; MAX_SPIRITCHARM as usize],
        ..Default::default()
    }
}

/// The character's skill fields (rAthena's zeroed defaults when the row does not exist yet).
pub fn get(c: &Ctx, aid: u32) -> PcSkill {
    c.db().pc_skill().account_id().find(aid).unwrap_or_else(|| fresh(aid))
}

pub fn put(c: &Ctx, s: &PcSkill) {
    match c.db().pc_skill().account_id().find(s.account_id) {
        Some(old) if old == *s => {}
        Some(_) => {
            c.db().pc_skill().account_id().update(s.clone());
        }
        None => {
            c.db().pc_skill().insert(s.clone());
        }
    }
}

/// Runs `f` on the character's row and stores it.
pub fn with<R>(c: &Ctx, aid: u32, f: impl FnOnce(&mut PcSkill) -> R) -> R {
    let mut s = get(c, aid);
    let r = f(&mut s);
    put(c, &s);
    r
}

/// pc.cpp pc_authok: `canequip_tick = canskill_tick = tick`, the spirit timers INVALID_TIMER, skill_keep_using
/// cleared (map_session_data starts zeroed; showdelay is atcmd_pc's).
pub fn pc_authok(c: &Ctx, aid: u32) {
    let mut s = fresh(aid);
    s.canequip_tick = c.tick();
    s.canskill_tick = c.tick();
    if c.db().pc_skill().account_id().find(aid).is_some() {
        c.db().pc_skill().account_id().update(s);
    } else {
        c.db().pc_skill().insert(s);
    }
}

/// unit_free: the row goes with the character (its timers die with the unit: timer::delete_for_target).
pub fn free(c: &Ctx, aid: u32) {
    c.db().pc_skill().account_id().delete(aid);
}

// ---------------------------------------------------------------------------------------------- spirit spheres

fn is_royal_guard(c: &Ctx, aid: u32) -> bool {
    use crate::db::jobs::{MAPID_ROYAL_GUARD, MAPID_THIRDMASK};
    crate::status::data::sd_get(c, aid).map(|s| s.class_ & MAPID_THIRDMASK == MAPID_ROYAL_GUARD).unwrap_or(false)
}

/// The sphere display after a change: clif_millenniumshield for Royal Guards, clif_spiritball otherwise.
fn show_spheres(c: &Ctx, aid: u32, n: i8) {
    let Some(u) = crate::unit::get(c, aid) else { return };
    if is_royal_guard(c, aid) {
        clif_millenniumshield(c, &u, n as i16);
    } else {
        super::clif::clif_spiritball(c, &u, None, Target::Area);
    }
}

/// clif.cpp clif_millenniumshield: ZC_MILLENNIUMSHIELD (PACKETVER >= 20081126) to the area.
pub fn clif_millenniumshield(c: &Ctx, bl: &Unit, shields: i16) {
    let p = PACKET_ZC_MILLENNIUMSHIELD { packetType: PACKET_ZC_MILLENNIUMSHIELD::ID as i16, aid: bl.id, num: shields, state: 0 };
    clif::send(c, &p.to_vec(), bl, Target::Area);
}

/// pc.cpp pc_spiritball_timer (Func::PcSpiritball): one sphere expires.
pub fn pc_spiritball_timer(c: &Ctx, tid: u64, aid: u32) {
    if crate::unit::get(c, aid).map(|u| u.kind != crate::map::BL_PC).unwrap_or(true) {
        return;
    }
    let mut s = get(c, aid);
    if s.spiritball <= 0 {
        log::error!("pc_spiritball_timer: {} spiritball's available. (aid={aid} tid={tid})", s.spiritball);
        s.spiritball = 0;
        put(c, &s);
        return;
    }
    let n = s.spiritball as usize;
    let Some(i) = s.spirit_timer[..n].iter().position(|&t| t == tid) else {
        log::error!("pc_spiritball_timer: timer not found (aid={aid} tid={tid})");
        return;
    };
    s.spiritball -= 1;
    let n = s.spiritball as usize;
    if i != n {
        s.spirit_timer.copy_within(i + 1..=n, i);
    }
    s.spirit_timer[n] = 0;
    put(c, &s);
    if let Some(u) = crate::unit::get(c, aid) {
        super::clif::clif_spiritball(c, &u, None, Target::Area);
    }
}

/// pc.cpp pc_addspiritball: a sphere for `interval` ms (the oldest one goes when `max` is reached).
pub fn pc_addspiritball(c: &Ctx, aid: u32, interval: i32, max: i32) {
    let max = max.min(MAX_SPIRITBALL);
    let mut s = get(c, aid);
    if s.spiritball < 0 {
        s.spiritball = 0;
    }
    if s.spiritball != 0 && s.spiritball as i32 >= max {
        if s.spirit_timer[0] != 0 {
            timer::delete(c, s.spirit_timer[0]);
        }
        s.spiritball -= 1;
        let n = s.spiritball as usize;
        if n != 0 {
            s.spirit_timer.copy_within(1..=n, 0);
        }
        s.spirit_timer[n] = 0;
    }
    let due = c.tick() + interval as i64;
    let tid = timer::add(c, due, Func::PcSpiritball, aid, 0);
    // ARR_FIND(0, spiritball, i, spirit_timer[i] == INVALID_TIMER || DIFF_TICK(due(tid), due(spirit_timer[i])) < 0)
    let n = s.spiritball as usize;
    let mut i = 0;
    while i < n {
        let t = s.spirit_timer[i];
        if t == 0 || timer::due(c, t).map(|d| due - d < 0).unwrap_or(false) {
            break;
        }
        i += 1;
    }
    if i != n {
        s.spirit_timer.copy_within(i..n, i + 1);
    }
    s.spirit_timer[i] = tid;
    s.spiritball += 1;
    let now = s.spiritball;
    put(c, &s);
    show_spheres(c, aid, now);
}

/// pc.cpp pc_delspiritball: removes `count` spheres (`type_` 1: no client effect).
pub fn pc_delspiritball(c: &Ctx, aid: u32, count: i32, type_: i32) {
    let mut s = get(c, aid);
    if s.spiritball <= 0 {
        s.spiritball = 0;
        put(c, &s);
        return;
    }
    if count == 0 {
        return;
    }
    let mut count = count.min(s.spiritball as i32);
    s.spiritball -= count as i8;
    if count > MAX_SPIRITBALL {
        count = MAX_SPIRITBALL;
    }
    let count = count as usize;
    for t in s.spirit_timer.iter_mut().take(count) {
        if *t != 0 {
            timer::delete(c, *t);
            *t = 0;
        }
    }
    for i in count..MAX_SPIRITBALL as usize {
        s.spirit_timer[i - count] = s.spirit_timer[i];
        s.spirit_timer[i] = 0;
    }
    let now = s.spiritball;
    put(c, &s);
    if type_ == 0 {
        show_spheres(c, aid, now);
    }
}

// ---------------------------------------------------------------------------------------------- souls, servants, abyss

/// clif.cpp clif_soulball: ZC_SOULENERGY (PACKETVER_RE_NUM >= 20200723) around the player or to `target`.
pub fn clif_soulball(c: &Ctx, sd: &Unit, target: Option<&Unit>, send_target: Target) {
    let p = PACKET_ZC_SOULENERGY { PacketType: PACKET_ZC_SOULENERGY::ID as i16, AID: sd.id, num: get(c, sd.id).soulball as u16 };
    clif::send(c, &p.to_vec(), target.unwrap_or(sd), send_target);
}

/// clif.cpp clif_servantball: ZC_SPIRITS with the servant count.
pub fn clif_servantball(c: &Ctx, sd: &Unit, target: Option<&Unit>, send_target: Target) {
    let p = PACKET_ZC_SPIRITS { PacketType: PACKET_ZC_SPIRITS::ID as i16, AID: sd.id, num: get(c, sd.id).servantball as i16 };
    clif::send(c, &p.to_vec(), target.unwrap_or(sd), send_target);
}

/// clif.cpp clif_abyssball: ZC_SPIRITS with the abyss sphere count.
pub fn clif_abyssball(c: &Ctx, sd: &Unit, target: Option<&Unit>, send_target: Target) {
    let p = PACKET_ZC_SPIRITS { PacketType: PACKET_ZC_SPIRITS::ID as i16, AID: sd.id, num: get(c, sd.id).abyssball as i16 };
    clif::send(c, &p.to_vec(), target.unwrap_or(sd), send_target);
}

/// pc.cpp pc_addsoulball.
pub fn pc_addsoulball(c: &Ctx, aid: u32, number: i32, max: i32) {
    use crate::status::consts::SC_SOULENERGY;
    let Some(u) = crate::unit::get(c, aid) else { return };
    let mut s = get(c, aid);
    // Save previous soulball number
    let val = s.soulball as i32;
    if !crate::status::sc::has_sce(c, aid, SC_SOULENERGY) {
        s.soulball = 0;
    }
    let max = max.min(MAX_SOUL_BALL);
    s.soulball = (s.soulball as i32 + number).clamp(0, max) as i8;
    let now = s.soulball as i32;
    put(c, &s);
    // Refresh the SC_SOULENERGY timer (which is 10 minutes by default) if the number has changed. The soul is removed
    // when the timer expire.
    if now != val {
        let d = crate::db::skills::skill_get_time2(crate::db::skills::SP_SOULCOLLECT, 1) as i64;
        crate::status::sc::sc_start(c, Some(&u), &u, SC_SOULENERGY, 100, now, d);
    }
    if let Some(u) = crate::unit::get(c, aid) {
        clif_soulball(c, &u, None, Target::Area);
    }
}

/// pc.cpp pc_delsoulball.
pub fn pc_delsoulball(c: &Ctx, aid: u32, count: i32, no_client_effect: bool) {
    use crate::status::consts::SC_SOULENERGY;
    if count <= 0 {
        return;
    }
    let mut s = get(c, aid);
    if s.soulball <= 0 {
        s.soulball = 0;
        put(c, &s);
    } else {
        s.soulball -= count.clamp(0, s.soulball as i32) as i8;
        let now = s.soulball as i32;
        put(c, &s);
        if now == 0 {
            crate::status::sc::status_change_end(c, aid, SC_SOULENERGY, 0);
        } else if let Some(u) = crate::unit::get(c, aid) {
            // refresh val1 and status MATK
            let d = crate::db::skills::skill_get_time2(crate::db::skills::SP_SOULCOLLECT, 1) as i64;
            crate::status::sc::sc_start(c, Some(&u), &u, SC_SOULENERGY, 100, now, d);
        }
    }
    if !no_client_effect {
        if let Some(u) = crate::unit::get(c, aid) {
            clif_soulball(c, &u, None, Target::Area);
        }
    }
}

/// pc.cpp pc_addservantball (count 1 by default).
pub fn pc_addservantball(c: &Ctx, aid: u32, count: i32) {
    with(c, aid, |s| s.servantball = (s.servantball as i32 + count).clamp(0, MAX_SERVANTBALL) as i8);
    if let Some(u) = crate::unit::get(c, aid) {
        clif_servantball(c, &u, None, Target::Area);
    }
}

/// pc.cpp pc_delservantball (count 1 by default).
pub fn pc_delservantball(c: &Ctx, aid: u32, count: i32) {
    with(c, aid, |s| s.servantball = (s.servantball as i32 - count).clamp(0, MAX_SERVANTBALL) as i8);
    if let Some(u) = crate::unit::get(c, aid) {
        clif_servantball(c, &u, None, Target::Area);
    }
}

/// pc.cpp pc_addabyssball (count 1 by default).
pub fn pc_addabyssball(c: &Ctx, aid: u32, count: i32) {
    with(c, aid, |s| s.abyssball = (s.abyssball as i32 + count).clamp(0, MAX_ABYSSBALL) as i8);
    if let Some(u) = crate::unit::get(c, aid) {
        clif_abyssball(c, &u, None, Target::Area);
    }
}

/// pc.cpp pc_delabyssball (count 1 by default).
pub fn pc_delabyssball(c: &Ctx, aid: u32, count: i32) {
    with(c, aid, |s| s.abyssball = (s.abyssball as i32 - count).clamp(0, MAX_ABYSSBALL) as i8);
    if let Some(u) = crate::unit::get(c, aid) {
        clif_abyssball(c, &u, None, Target::Area);
    }
}

// ---------------------------------------------------------------------------------------------- spirit charms

/// clif.cpp clif_spiritcharm: ZC_SPIRITS_ATTRIBUTE to the area.
pub fn clif_spiritcharm(c: &Ctx, sd: &Unit) {
    let s = get(c, sd.id);
    let p = PACKET_ZC_SPIRITS_ATTRIBUTE { packetType: PACKET_ZC_SPIRITS_ATTRIBUTE::ID as i16, aid: sd.id, spiritsType: s.spiritcharm_type as i16, num: s.spiritcharm };
    clif::send(c, &p.to_vec(), sd, Target::Area);
}

fn show_charms(c: &Ctx, aid: u32) {
    if let Some(u) = crate::unit::get(c, aid) {
        clif_spiritcharm(c, &u);
    }
}

/// pc.cpp pc_spiritcharm_timer (Func::PcSpiritcharm).
pub fn pc_spiritcharm_timer(c: &Ctx, tid: u64, aid: u32) {
    if crate::unit::get(c, aid).map(|u| u.kind != crate::map::BL_PC).unwrap_or(true) {
        return;
    }
    let mut s = get(c, aid);
    if s.spiritcharm <= 0 {
        log::error!("pc_spiritcharm_timer: {} spiritcharm's available. (aid={aid} tid={tid})", s.spiritcharm);
        s.spiritcharm = 0;
        s.spiritcharm_type = CHARM_TYPE_NONE;
        put(c, &s);
        return;
    }
    let n = s.spiritcharm as usize;
    let Some(i) = s.spiritcharm_timer[..n].iter().position(|&t| t == tid) else {
        log::error!("pc_spiritcharm_timer: timer not found (aid={aid} tid={tid})");
        return;
    };
    s.spiritcharm -= 1;
    let n = s.spiritcharm as usize;
    if i != n {
        s.spiritcharm_timer.copy_within(i + 1..=n, i);
    }
    s.spiritcharm_timer[n] = 0;
    if s.spiritcharm <= 0 {
        s.spiritcharm_type = CHARM_TYPE_NONE;
    }
    put(c, &s);
    show_charms(c, aid);
}

/// pc.cpp pc_addspiritcharm.
pub fn pc_addspiritcharm(c: &Ctx, aid: u32, interval: i32, max: i32, type_: i32) {
    let cur = get(c, aid);
    if cur.spiritcharm_type != CHARM_TYPE_NONE && type_ != cur.spiritcharm_type {
        pc_delspiritcharm(c, aid, cur.spiritcharm as i32, cur.spiritcharm_type);
    }
    let max = max.min(MAX_SPIRITCHARM);
    let mut s = get(c, aid);
    if s.spiritcharm < 0 {
        s.spiritcharm = 0;
    }
    if s.spiritcharm != 0 && s.spiritcharm as i32 >= max {
        if s.spiritcharm_timer[0] != 0 {
            timer::delete(c, s.spiritcharm_timer[0]);
        }
        s.spiritcharm -= 1;
        let n = s.spiritcharm as usize;
        if n != 0 {
            s.spiritcharm_timer.copy_within(1..=n, 0);
        }
        s.spiritcharm_timer[n] = 0;
    }
    let due = c.tick() + interval as i64;
    let tid = timer::add(c, due, Func::PcSpiritcharm, aid, 0);
    let n = s.spiritcharm as usize;
    let mut i = 0;
    while i < n {
        let t = s.spiritcharm_timer[i];
        if t == 0 || timer::due(c, t).map(|d| due - d < 0).unwrap_or(false) {
            break;
        }
        i += 1;
    }
    if i != n {
        s.spiritcharm_timer.copy_within(i..n, i + 1);
    }
    s.spiritcharm_timer[i] = tid;
    s.spiritcharm += 1;
    s.spiritcharm_type = type_;
    put(c, &s);
    show_charms(c, aid);
}

/// pc.cpp pc_delspiritcharm.
pub fn pc_delspiritcharm(c: &Ctx, aid: u32, count: i32, type_: i32) {
    let mut s = get(c, aid);
    if s.spiritcharm_type != type_ {
        return;
    }
    if s.spiritcharm <= 0 {
        s.spiritcharm = 0;
        put(c, &s);
        return;
    }
    if count <= 0 {
        return;
    }
    let mut count = count.min(s.spiritcharm as i32);
    s.spiritcharm -= count as i16;
    if count > MAX_SPIRITCHARM {
        count = MAX_SPIRITCHARM;
    }
    let count = count as usize;
    for t in s.spiritcharm_timer.iter_mut().take(count) {
        if *t != 0 {
            timer::delete(c, *t);
            *t = 0;
        }
    }
    for i in count..MAX_SPIRITCHARM as usize {
        s.spiritcharm_timer[i - count] = s.spiritcharm_timer[i];
        s.spiritcharm_timer[i] = 0;
    }
    if s.spiritcharm <= 0 {
        s.spiritcharm_type = CHARM_TYPE_NONE;
    }
    put(c, &s);
    show_charms(c, aid);
}

// ---------------------------------------------------------------------------------------------- area entry

/// clif.cpp clif_millenniumshield_single: the owner's Millennium Shields (SC_MILLENNIUMSHIELD's val2) to one player.
fn clif_millenniumshield_single(c: &Ctx, owner: u32, tsd: &Unit) {
    let Some(sce) = crate::status::sc::get_sce(c, owner, crate::status::consts::SC_MILLENNIUMSHIELD) else { return };
    let p = PACKET_ZC_MILLENNIUMSHIELD { packetType: PACKET_ZC_MILLENNIUMSHIELD::ID as i16, aid: owner, num: sce.val2 as i16, state: 0 };
    clif::send(c, &p.to_vec(), tsd, Target::SelfOnly);
}

/// clif.cpp clif_spiritcharm_single: the owner's spirit charms to one player (none without a charm type or charms).
fn clif_spiritcharm_single(c: &Ctx, owner: u32, tsd: &Unit) {
    let s = get(c, owner);
    if s.spiritcharm_type == CHARM_TYPE_NONE {
        return;
    }
    if s.spiritcharm <= 0 {
        return;
    }
    let p = PACKET_ZC_SPIRITS_ATTRIBUTE { packetType: PACKET_ZC_SPIRITS_ATTRIBUTE::ID as i16, aid: owner, spiritsType: s.spiritcharm_type as i16, num: s.spiritcharm };
    clif::send(c, &p.to_vec(), tsd, Target::SelfOnly);
}

// ---------------------------------------------------------------------------------------------- Crimson Marker

/// pc.cpp pc_crimson_marker_clear: every target the player marked with Crimson Marker (RL_C_MARKER) loses its
/// SC_C_MARKER (when it is still in the world: map_id2bl), and the slots are emptied. Called by map_quit.
pub fn pc_crimson_marker_clear(c: &Ctx, aid: u32) {
    if crate::unit::get(c, aid).map(|u| u.kind != crate::map::BL_PC).unwrap_or(true) {
        return; // if (!sd) return;
    }
    for i in 0..MAX_SKILL_CRIMSON_MARKER {
        // (each slot read fresh: SC_C_MARKER's end also empties the caster's slot, status.cpp:13947-13949)
        let id = get(c, aid).c_marker.get(i).copied().unwrap_or(0);
        if id != 0 && crate::unit::get(c, id).is_some() {
            crate::status::sc::status_change_end(c, id, crate::status::consts::SC_C_MARKER, 0);
        }
        with(c, aid, |s| {
            if let Some(slot) = s.c_marker.get_mut(i) {
                *slot = 0;
            }
        });
    }
}

/// clif.cpp clif_getareachar_pc, the sphere part: what `sd` is shown of `dstsd` coming into view (spirit spheres,
/// Millennium Shields, spirit charms, soul energy, servant weapons, abyss spheres), each to `sd` only.
pub fn getareachar_pc_spheres(c: &Ctx, sd: &Unit, dstsd: &Unit) {
    let d = get(c, dstsd.id);
    if d.spiritball > 0 {
        super::clif::clif_spiritball(c, dstsd, Some(sd), Target::SelfOnly);
    }
    clif_millenniumshield_single(c, dstsd.id, sd);
    clif_spiritcharm_single(c, dstsd.id, sd);
    if d.soulball > 0 {
        clif_soulball(c, dstsd, Some(sd), Target::SelfOnly);
    }
    if d.servantball > 0 {
        clif_servantball(c, dstsd, Some(sd), Target::SelfOnly);
    }
    if d.abyssball > 0 {
        clif_abyssball(c, dstsd, Some(sd), Target::SelfOnly);
    }
}

/// clif.cpp clif_getareachar_pc, the devotion part: the link sd - dstsd (when sd devotes dstsd), dstsd's links to its
/// devotees, and the link from dstsd's crusader (SC_DEVOTION's val1), each shown to `sd`.
pub fn getareachar_pc_devotion(c: &Ctx, sd: &Unit, dstsd: &Unit) {
    // display link (sd - dstsd) to sd
    if get(c, sd.id).devotion.iter().take(MAX_DEVOTION).any(|&d| d == dstsd.id) {
        super::clif::clif_devotion(c, sd, Some(sd));
    }
    // display links (dstsd - devotees) to sd
    if get(c, dstsd.id).devotion.iter().take(MAX_DEVOTION).any(|&d| d as i32 > 0) {
        super::clif::clif_devotion(c, dstsd, Some(sd));
    }
    // display link (dstsd - crusader) to sd
    if let Some(sce) = crate::status::sc::get_sce(c, dstsd.id, crate::status::consts::SC_DEVOTION) {
        if let Some(d_bl) = crate::unit::get(c, sce.val1 as u32) {
            super::clif::clif_devotion(c, &d_bl, Some(sd));
        }
    }
}

/// clif.cpp clif_spawn (BL_PC), the sphere part before the size effects: spirit spheres, Millennium Shields
/// (SC_MILLENNIUMSHIELD's val2), soul energy, servant weapons and abyss spheres, each to the area.
pub fn spawn_pc_spheres(c: &Ctx, sd: &Unit) {
    let s = get(c, sd.id);
    if s.spiritball > 0 {
        super::clif::clif_spiritball(c, sd, None, Target::Area);
    }
    if let Some(sce) = crate::status::sc::get_sce(c, sd.id, crate::status::consts::SC_MILLENNIUMSHIELD) {
        clif_millenniumshield(c, sd, sce.val2 as i16);
    }
    if s.soulball > 0 {
        clif_soulball(c, sd, None, Target::Area);
    }
    if s.servantball > 0 {
        clif_servantball(c, sd, None, Target::Area);
    }
    if s.abyssball > 0 {
        clif_abyssball(c, sd, None, Target::Area);
    }
}

/// clif.cpp clif_spawn (BL_PC), after the battleground emblem: the spirit charms when the player has a charm type
/// and charms.
pub fn spawn_pc_charms(c: &Ctx, sd: &Unit) {
    let s = get(c, sd.id);
    if s.spiritcharm_type != CHARM_TYPE_NONE && s.spiritcharm > 0 {
        clif_spiritcharm(c, sd);
    }
}
