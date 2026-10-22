//! skill.cpp unit groups and units: skill_get_new_group_id, skill_initunitgroup, skill_delunitgroup_,
//! skill_clear_unitgroup, skill_initunit, skill_delunit, skill_unitgrouptickset_search, skill_clear_group,
//! skill_locate_element_field; unit.cpp unit_skillunit_maxcount.

use spacetimedb::Table;

use crate::db::skills::{self as sk, *};
use crate::map::{BL_MOB, BL_NPC, BL_PC};
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::consts::*;
use super::data::{self, skill_unit_group as _, SkillUnit, SkillUnitGroup};
use crate::map::block_seq as _;
use crate::unit::unit as _;

/// unit.cpp unit_bl2ud: the block_list kinds that have a unit_data (and so can own groups and ticksets).
pub fn has_ud(kind: u16) -> bool {
    use crate::map::{BL_ELEM, BL_HOM, BL_MER, BL_PET};
    matches!(kind, BL_PC | BL_MOB | BL_PET | BL_NPC | BL_HOM | BL_MER | BL_ELEM)
}

/// skill.cpp skill_get_new_group_id: the next group id not in use (from MAX_SKILL up, wrapping).
fn skill_get_new_group_id(c: &Ctx) -> i32 {
    let mut s = data::state(c);
    let max_skill = MAX_SKILL as i32;
    let taken = |id: i32| data::skill_id2group(c, id).is_some();
    let id = if s.group_newid >= max_skill && !taken(s.group_newid) {
        let id = s.group_newid;
        s.group_newid = s.group_newid.wrapping_add(1);
        id
    } else {
        // find next id
        let base_id = s.group_newid;
        loop {
            s.group_newid = s.group_newid.wrapping_add(1);
            if s.group_newid == base_id {
                // full loop, nothing available ("skill_get_new_group_id: All ids are taken. Exiting...": rAthena exits)
                log::error!("skill_get_new_group_id: All ids are taken.");
                data::put_state(c, &s);
                return 0;
            }
            if s.group_newid < max_skill {
                s.group_newid = max_skill;
            }
            if !taken(s.group_newid) {
                let id = s.group_newid;
                s.group_newid = s.group_newid.wrapping_add(1);
                break id;
            }
        }
    };
    data::put_state(c, &s);
    id
}

/// skill.cpp skill_initunitgroup: a new group for `count` units of `src`'s skill (the unit array starts dead).
#[allow(clippy::too_many_arguments)]
pub fn skill_initunitgroup(c: &Ctx, src: &Unit, count: i32, skill_id: u16, skill_lv: u16, unit_id: i32, limit: i64, interval: i32) -> Option<SkillUnitGroup> {
    if !has_ud(src.kind) {
        return None; // nullpo_retr(nullptr, ud)
    }
    if skill_id == 0 || skill_lv == 0 {
        return None;
    }
    let group_id = skill_get_new_group_id(c);
    let mut s = data::state(c);
    let seq = s.next_seq;
    s.next_seq += 1;
    data::put_state(c, &s);
    let g = SkillUnitGroup {
        group_id,
        seq,
        src_id: src.id,
        party_id: crate::battle::target::status_get_party_id(c, src),
        guild_id: crate::battle::target::status_get_guild_id(c, src),
        bg_id: super::iface::bg_team_get_id(c, src),
        map: src.map,
        target_flag: 0,
        bl_flag: 0,
        tick: c.tick(),
        limit,
        interval,
        skill_id,
        skill_lv,
        val1: 0,
        val2: 0,
        val3: 0,
        valstr: None,
        unit_id,
        link_group_id: 0,
        unit_count: count,
        alive_count: 0,
        item_id: 0,
        ammo_consume: false,
        song_dance: 0,
        guildaura: false,
    };
    // ud->skillunits.push_back(group); skillunit_group_db.insert
    c.db().skill_unit_group().insert(g.clone());
    // skill_unit_timer runs every SKILLUNITTIMER_INTERVAL from do_init_skill on; here it is armed with the first group
    super::timer::ensure_unit_timer(c);
    Some(g)
}

/// skill.cpp skill_delunitgroup_ (skill_delunitgroup).
pub fn skill_delunitgroup(c: &Ctx, group_id: i32) -> i32 {
    let Some(group) = data::skill_id2group(c, group_id) else {
        log::debug!("skill_delunitgroup: group {group_id} is nullptr!");
        return 0;
    };
    let src = unit::get(c, group.src_id);
    let Some(src) = src.filter(|s| has_ud(s.kind)) else {
        log::error!("skill_delunitgroup: Group's source not found! (src_id: {} skill_id: {})", group.src_id, group.skill_id);
        return 0;
    };
    // `((TBL_PC*)src)->state.warping && !...changemap` reads map_session_data fields through a cast for any source
    // type; only a player has them (for a monster or NPC rAthena reads unrelated memory there)
    if src.kind == BL_PC && !sc::status_isdead(c, &src) {
        let (warping, changemap) = crate::pc::get(c, src.id).map(|p| (p.warping, p.changemap)).unwrap_or((false, false));
        if warping && !changemap {
            match group.skill_id {
                BA_DISSONANCE | BA_POEMBRAGI | BA_WHISTLE | BA_ASSASSINCROSS | BA_APPLEIDUN | DC_UGLYDANCE | DC_HUMMING
                | DC_DONTFORGETME | DC_FORTUNEKISS | DC_SERVICEFORYOU | NC_NEUTRALBARRIER | NC_STEALTHFIELD => {
                    super::castend::skill_usave_add(c, src.id, group.skill_id, group.skill_lv);
                }
                _ => {}
            }
        }
    }
    if skill_get_unit_flag(group.skill_id, &[UF_DANCE, UF_SONG, UF_ENSEMBLE]) {
        if let Some(mut sce) = sc::get_sce(c, src.id, SC_DANCING) {
            sce.val2 = 0; // This prevents status_change_end attempting to redelete the group. [Skotlex]
            sc::put_sce(c, &sce);
            sc::status_change_end(c, src.id, SC_DANCING, 0);
        }
    }
    // End SC from the master when the skill group is deleted (UNT_BASILICA: pre-renewal only)
    if group.unit_id == UNT_GOSPEL {
        if let Some(mut sce) = sc::get_sce(c, src.id, SC_GOSPEL) {
            sce.val3 = 0; // Remove reference to this group. [Skotlex]
            sc::put_sce(c, &sce);
            sc::status_change_end(c, src.id, SC_GOSPEL, 0);
        }
    }
    match group.skill_id {
        PF_SPIDERWEB => {
            if let Some(target) = unit::get(c, group.val2 as u32) {
                if let Some(mut sce) = sc::get_sce(c, target.id, SC_SPIDERWEB) {
                    // Clear group id from status change
                    let mut removed = true;
                    if sce.val2 == group.group_id {
                        sce.val2 = 0;
                    } else if sce.val3 == group.group_id {
                        sce.val3 = 0;
                    } else if sce.val4 == group.group_id {
                        sce.val4 = 0;
                    } else {
                        removed = false; // Group was already removed in status_change_end, don't call it again!
                    }
                    sc::put_sce(c, &sce);
                    // The last group was cleared, end status change
                    if removed && sce.val2 == 0 && sce.val3 == 0 && sce.val4 == 0 {
                        sc::status_change_end(c, target.id, SC_SPIDERWEB, 0);
                    }
                }
            }
        }
        SG_SUN_WARM | SG_MOON_WARM | SG_STAR_WARM => {
            if let Some(mut sce) = sc::get_sce(c, src.id, SC_WARM) {
                sce.val4 = 0;
                sc::put_sce(c, &sce);
                sc::status_change_end(c, src.id, SC_WARM, 0);
            }
        }
        LG_BANDING => {
            if let Some(mut sce) = sc::get_sce(c, src.id, SC_BANDING) {
                sce.val4 = 0;
                sc::put_sce(c, &sce);
                sc::status_change_end(c, src.id, SC_BANDING, 0);
            }
        }
        NC_NEUTRALBARRIER => {
            if let Some(mut sce) = sc::get_sce(c, src.id, SC_NEUTRALBARRIER_MASTER) {
                sce.val2 = 0;
                sc::put_sce(c, &sce);
                sc::status_change_end(c, src.id, SC_NEUTRALBARRIER_MASTER, 0);
            }
            sc::status_change_end(c, src.id, SC_NEUTRALBARRIER, 0);
        }
        NC_STEALTHFIELD => {
            if let Some(mut sce) = sc::get_sce(c, src.id, SC_STEALTHFIELD_MASTER) {
                sce.val2 = 0;
                sc::put_sce(c, &sce);
                sc::status_change_end(c, src.id, SC_STEALTHFIELD_MASTER, 0);
            }
        }
        _ => {}
    }
    let group = data::skill_id2group(c, group_id).unwrap_or(group);
    if src.kind == BL_PC && group.ammo_consume {
        crate::battle::weapon_attack::battle_consume_ammo(c, src.id, group.skill_id, group.skill_lv);
    }
    data::upd_group(c, group_id, |g| g.alive_count = 0);
    // remove all unit cells
    for su in data::units_of(c, group_id) {
        skill_delunit(c, su.id());
    }
    // clear Talkie-box string; skillunit_group_db.erase; util::vector_erase_if_exists(ud->skillunits, group)
    let link_group_id = data::skill_id2group(c, group_id).map(|g| g.link_group_id).unwrap_or(group.link_group_id);
    if c.db().skill_unit_group().group_id().find(group_id).is_some() {
        c.db().skill_unit_group().group_id().delete(group_id);
    } else {
        log::error!("skill_delunitgroup: Group not found! (src_id: {} skill_id: {})", group.src_id, group.skill_id);
    }
    if link_group_id != 0 && data::skill_id2group(c, link_group_id).is_some() {
        skill_delunitgroup(c, link_group_id);
    }
    1
}

/// skill.cpp skill_clear_unitgroup: every group of `src` goes (logout, death, warp …).
pub fn skill_clear_unitgroup(c: &Ctx, src_id: u32) {
    // unit_bl2ud (nullpo_retv): only units that have a unit_data own groups
    loop {
        let Some(g) = data::groups_of(c, src_id).into_iter().next() else { break };
        let before = data::groups_of(c, src_id).len();
        skill_delunitgroup(c, g.group_id);
        if data::groups_of(c, src_id).len() >= before {
            // rAthena would loop forever on a group it cannot delete (its source is not found); stop instead
            log::error!("skill_clear_unitgroup: group {} of {src_id} could not be deleted", g.group_id);
            break;
        }
    }
}

/// skill.cpp skill_unitgrouptickset_search + the caller's use of it (skill_unit_onplace_timer): None when the unit
/// has no tickset (no unit_data, or the group has no interval), else Some(diff) with `diff = tick - ts->tick` of the
/// found (or newly claimed) entry; a non-negative diff re-arms the entry to `tick + interval` as the caller does.
pub fn tickset_check(c: &Ctx, bl: &Unit, group: &SkillUnitGroup, tick: Tick) -> Option<i64> {
    if group.interval == -1 {
        return None;
    }
    if !has_ud(bl.kind) {
        return None;
    }
    let mut set = data::tickset(c, bl.id);
    let id = if skill_get_unit_flag(group.skill_id, &[UF_NOOVERLAP]) { group.skill_id as i32 } else { group.group_id };
    let s = id;
    let n = MAX_SKILLUNITGROUPTICKSET as i32;
    let mut j: i32 = -1;
    let mut found = None;
    for i in 0..n {
        let k = ((i + s) % n) as usize;
        if set.id[k] == id {
            found = Some(k);
            break;
        } else if j == -1 && (tick - set.tick[k] > 0 || set.id[k] == 0) {
            j = k as i32;
        }
    }
    let k = match found {
        Some(k) => k,
        None => {
            if j == -1 {
                log::warn!("skill_unitgrouptickset_search: tickset is full");
                j = id % n;
            }
            let k = j as usize;
            set.id[k] = id;
            set.tick[k] = tick;
            k
        }
    };
    let diff = tick - set.tick[k];
    if diff >= 0 {
        set.tick[k] = tick + group.interval as i64;
    }
    data::put_tickset(c, &set);
    Some(diff)
}

/// The outcome of skill_initunit.
pub enum InitUnit {
    /// The unit is placed and alive.
    Placed(SkillUnit),
    /// A Maelstrom cell: rAthena returns the unit slot untouched (dead, zeroed: limit 0, map 0 cell 0,0).
    NotPlaced,
    /// map_addblock failed (nullptr).
    Failed,
}

/// skill.cpp skill_initunit: unit `idx` of the group goes on (x, y).
#[allow(clippy::too_many_arguments)]
pub fn skill_initunit(c: &Ctx, group_id: i32, idx: i32, x: i16, y: i16, val1: i32, val2: i32, hidden: bool, range: i32, limit: i64) -> InitUnit {
    let Some(group) = data::skill_id2group(c, group_id) else { return InitUnit::Failed };
    let src_m = unit::get(c, group.src_id).map(|s| s.map).unwrap_or(group.map);
    if crate::map_cell::getcell(c, Some(src_m), x, y, crate::map_cell::CELL_CHKMAELSTROM) != 0 {
        return InitUnit::NotPlaced;
    }
    // (the slot was dead: aCalloc'd, or cleared by skill_delunit)
    data::upd_group(c, group_id, |g| g.alive_count += 1);
    let id = crate::map::get_new_object_id(c);
    let bl = Unit {
        id,
        kind: crate::map::BL_SKILL,
        map: group.map,
        x,
        y,
        on_map: false,
        name: String::new(),
        vd: Default::default(),
        ud: Default::default(),
        sc: Default::default(),
        speed: 0,
        mode: 0,
        // status_get_lv's default for a BL_SKILL
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
    let mut su = SkillUnit { bl, group_id, idx, limit, val1, val2, range: range as i16, alive: true, hidden };
    // idb_put(skillunit_db, unit->id, unit); map_addiddb(unit)
    super::unitdb::put(c, id);
    c.db().unit().insert(su.bl.clone());
    data::insert_unit(c, &su);
    if !unit::addblock(c, &mut su.bl) {
        return InitUnit::Failed;
    }
    // Perform oninit actions
    match group.skill_id {
        WZ_ICEWALL => {
            crate::map_cell::setgatcell(c, su.bl.map, x, y, 5);
            super::clif::clif_changemapcell(c, su.bl.map, x, y, 5, crate::clif::Target::Area, &su.bl);
            super::setting::skill_unitsetmapcell(c, &su, WZ_ICEWALL, group.skill_lv, crate::map_cell::CELL_ICEWALL, true);
        }
        SA_LANDPROTECTOR => super::setting::skill_unitsetmapcell(c, &su, SA_LANDPROTECTOR, group.skill_lv, crate::map_cell::CELL_LANDPROTECTOR, true),
        // (HP_BASILICA: pre-renewal only)
        SC_MAELSTROM => super::setting::skill_unitsetmapcell(c, &su, SC_MAELSTROM, group.skill_lv, crate::map_cell::CELL_MAELSTROM, true),
        _ => {
            if group.song_dance & 0x1 != 0 {
                // Check for dissonance.
                super::setting::skill_dance_overlap(c, id, OVERLAP_SET);
            }
        }
    }
    let Some(su) = data::get_unit(c, id) else { return InitUnit::Failed };
    super::clif::skill_getareachar_skillunit_visibilty(c, &su, crate::clif::Target::Area);
    InitUnit::Placed(su)
}

/// skill.cpp skill_delunit.
pub fn skill_delunit(c: &Ctx, unit_id: u32) -> i32 {
    let Some(mut su) = data::get_unit(c, unit_id) else { return 0 };
    if !su.alive {
        return 0;
    }
    su.alive = false;
    data::put_unit(c, &su);
    let Some(group) = data::skill_id2group(c, su.group_id) else { return 0 };
    if group.song_dance & 0x1 != 0 {
        // Cancel dissonance effect.
        super::setting::skill_dance_overlap(c, unit_id, OVERLAP_REMOVE);
    }
    // invoke onout event
    if su.range == 0 {
        super::effect::foreachincell_unit_effect(c, &su, su.bl.map, su.bl.x, su.bl.y, group.bl_flag, c.tick(), 4);
    }
    let su = data::get_unit(c, unit_id).unwrap_or(su);
    let group = data::skill_id2group(c, su.group_id).unwrap_or(group);
    // perform ondelete actions
    match group.skill_id {
        HT_ANKLESNARE | sk::SC_ESCAPE => {
            if let Some(target) = unit::get(c, group.val2 as u32) {
                sc::status_change_end(c, target.id, SC_ANKLE, 0);
            }
        }
        WZ_ICEWALL => {
            crate::map_cell::setgatcell(c, su.bl.map, su.bl.x, su.bl.y, su.val2);
            // Hack to avoid clientside cell bug
            super::clif::clif_changemapcell(c, su.bl.map, su.bl.x, su.bl.y, su.val2 as i16, crate::clif::Target::AllSamemap, &su.bl);
            super::setting::skill_unitsetmapcell(c, &su, WZ_ICEWALL, group.skill_lv, crate::map_cell::CELL_ICEWALL, false);
        }
        SA_LANDPROTECTOR => super::setting::skill_unitsetmapcell(c, &su, SA_LANDPROTECTOR, group.skill_lv, crate::map_cell::CELL_LANDPROTECTOR, false),
        // (HP_BASILICA: pre-renewal only)
        RA_ELECTRICSHOCKER => {
            if let Some(target) = unit::get(c, group.val2 as u32) {
                sc::status_change_end(c, target.id, SC_ELECTRICSHOCKER, 0);
            }
        }
        SC_MAELSTROM => super::setting::skill_unitsetmapcell(c, &su, SC_MAELSTROM, group.skill_lv, crate::map_cell::CELL_MAELSTROM, false),
        sk::SC_MANHOLE => {
            // Note : Removing the unit don't remove the status (official info)
            if group.val2 != 0 {
                // Someone Traped
                if let Some(mut sce) = sc::get_sce(c, group.val2 as u32, SC__MANHOLE) {
                    sce.val4 = 0; // Remove the Unit ID
                    sc::put_sce(c, &sce);
                }
            }
        }
        _ => {}
    }
    super::clif::clif_skill_delunit(c, &su);
    // unit->group = nullptr; map_delblock(unit); map_deliddb(unit); idb_remove(skillunit_db, unit->id)
    c.db().unit().id().delete(unit_id);
    c.db().block_seq().id().delete(unit_id);
    crate::map::block_seq_removed(unit_id);
    data::delete_unit_row(c, unit_id);
    super::unitdb::remove(c, unit_id);
    let mut alive = 0;
    data::upd_group(c, group.group_id, |g| {
        g.alive_count -= 1;
        alive = g.alive_count;
    });
    if alive == 0 && data::skill_id2group(c, group.group_id).is_some() {
        skill_delunitgroup(c, group.group_id);
    }
    0
}

/// skill.cpp skill_clear_group: `bl`'s groups of the kinds `flag` names go (1: elemental fields and friends, 2: traps,
/// 4: Cloud Kill, 8: Warmer). Returns how many.
pub fn skill_clear_group(c: &Ctx, bl_id: u32, flag: u8) -> i32 {
    let Some(bl) = unit::get(c, bl_id) else { return 0 };
    if !has_ud(bl.kind) {
        return 0;
    }
    let mut count = 0;
    // "The after loop statement might look stupid, but this prevents iteration problems, if an entry was deleted":
    // after a deletion the walk starts over from the first group
    let mut i = 0;
    loop {
        let groups = data::groups_of(c, bl_id);
        let Some(g) = groups.get(i) else { break };
        let del = match g.skill_id {
            SA_DELUGE | SA_VOLCANO | SA_VIOLENTGALE | SA_LANDPROTECTOR | NJ_SUITON | NJ_KAENSIN | sk::SC_CHAOSPANIC | MH_POISON_MIST
            | MH_LAVA_SLIDE | SOA_TOTEM_OF_TUTELARY => flag & 1 != 0,
            SO_CLOUD_KILL | NPC_CLOUD_KILL => flag & 4 != 0,
            SO_WARMER => flag & 8 != 0,
            _ => flag & 2 != 0 && skill_get_inf2(g.skill_id, &[INF2_ISTRAP]),
        };
        if del {
            skill_delunitgroup(c, g.group_id);
            count += 1;
            i = 0;
        } else {
            i += 1;
        }
    }
    count
}

/// skill.cpp skill_locate_element_field: the first of `bl`'s groups that is an element field (or one of the other
/// fields placed one at a time).
pub fn skill_locate_element_field(c: &Ctx, bl_id: u32) -> Option<SkillUnitGroup> {
    let bl = unit::get(c, bl_id)?;
    if !has_ud(bl.kind) {
        return None;
    }
    data::groups_of(c, bl_id).into_iter().find(|su| {
        matches!(
            su.skill_id,
            SA_DELUGE | SA_VOLCANO | SA_VIOLENTGALE | SA_LANDPROTECTOR | NJ_SUITON | SO_CLOUD_KILL | NPC_CLOUD_KILL | SO_WARMER
                | sk::SC_CHAOSPANIC | MH_POISON_MIST | MH_LAVA_SLIDE | SOA_TOTEM_OF_TUTELARY
        )
    })
}

/// unit.cpp unit_skillunit_maxcount: `maxcount` less one for each of the unit's groups of `skill_id`, stopping at 0.
pub fn unit_skillunit_maxcount(c: &Ctx, src_id: u32, skill_id: u16, maxcount: i32) -> i32 {
    let mut maxcount = maxcount;
    for su in data::groups_of(c, src_id) {
        if su.skill_id == skill_id {
            maxcount -= 1;
            if maxcount == 0 {
                break;
            }
        }
    }
    maxcount
}
