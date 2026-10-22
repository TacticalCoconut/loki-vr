//! map/skill.cpp's ground skill units (rAthena e985006, RENEWAL): unit groups and units, skill_unitsetting, what a
//! unit does to what stands on it (onplace / onplace_timer / onout / onleft / ondamaged), the unit timer, units and
//! characters moving (skill_unit_move*), traps, songs / dances / ensembles, the cast-end side of ground skills
//! (skill_castend_pos2's shell, skill_castend_map and its menus), the BL_SKILL block_list objects and their packets,
//! and the map cells units set (Ice Wall, Land Protector, Maelstrom).
//!
//! Submodules: `data` (the tables), `unitdb` (skillunit_db's iteration order), `group` (groups / units),
//! `setting` (skill_unitsetting, cell overlap, dissonance), `effect` (onplace / onout / onleft / ondamaged),
//! `ontimer` (onplace_timer, traps), `timer` (skill_unit_timer), `movement` (skill_unit_move*, map_moveblock's unit
//! part), `clif` (the packets and hidden-trap visibility), `castend` (castend_pos2 / castend_map / partners / songs),
//! `buildin` (warpportal), `iface` (what other agents' systems provide: stubs until their commits are picked up),
//! `conf`, `consts`, `util`, `atcommand` (@displayskillunit).

pub mod atcommand;
pub mod buildin;
pub mod castend;
pub mod clif;
pub mod conf;
pub mod consts;
pub mod data;
pub mod effect;
pub mod group;
pub mod iface;
pub mod movement;
pub mod ontimer;
pub mod setting;
pub mod timer;
pub mod unitdb;
pub mod util;
#[cfg(test)]
mod tests;

pub use castend::{skill_castend_map, skill_castend_pos2, skill_castend_song, skill_check_pc_partner, skill_pos_maxcount_check};
pub use data::{group_of_unit, skill_id2group, SkillUnit, SkillUnitGroup};
pub use effect::skill_unit_ondamaged;
pub use group::{skill_clear_group, skill_clear_unitgroup, skill_delunit, skill_delunitgroup, skill_locate_element_field, unit_skillunit_maxcount};
pub use movement::{map_find_skill_unit_oncell, skill_unit_move, skill_unit_move_id, skill_unit_move_unit, skill_unit_move_unit_group};
pub use setting::skill_unitsetting;

use spacetimedb::Table;

use crate::unit::Unit;
use crate::world::Ctx;

use data::*;

/// battle.cpp battle_get_master's BL_SKILL step: the unit's group source (None when the unit has no group or its
/// source id is 0).
pub fn master_of(c: &Ctx, bl: &Unit) -> Option<u32> {
    group_of_unit(c, bl.id).map(|g| g.src_id).filter(|&id| id != 0)
}

/// battle.cpp battle_getcurrentskill for a BL_SKILL: its group's skill (0 without a group).
pub fn current_skill(c: &Ctx, bl: &Unit) -> u16 {
    group_of_unit(c, bl.id).map(|g| g.skill_id).unwrap_or(0)
}

/// status.cpp status_get_party_id / status_get_guild_id for a BL_SKILL: its group's.
pub fn party_id(c: &Ctx, bl: &Unit) -> i32 {
    group_of_unit(c, bl.id).map(|g| g.party_id).unwrap_or(0)
}

pub fn guild_id(c: &Ctx, bl: &Unit) -> i32 {
    group_of_unit(c, bl.id).map(|g| g.guild_id).unwrap_or(0)
}

/// battleground.cpp bg_team_get_id's BL_SKILL arm: `su->group->bg_id` (0 without a group).
pub fn bg_id(c: &Ctx, bl: &Unit) -> i32 {
    group_of_unit(c, bl.id).map(|g| g.bg_id).unwrap_or(0)
}

/// battle.cpp battle_check_target, `case BL_SKILL` of the actual target: Err(return value) when the check ends there
/// (0: never a target, -1: not this time), Ok(()) when the unit is a target (the caller then sets BCT_ENEMY and keeps
/// it). Only traps (not yet used), Ice Wall and Wall of Thorn can be targeted, by the skills allowed to.
pub fn check_target_unit(c: &Ctx, src: &Unit, target: &Unit, flag: i32) -> Result<(), i32> {
    use crate::db::skills::*;
    use crate::npc::{mapflag, MF_GVG, MF_PVP};
    let skill_id = crate::battle::target::battle_getcurrentskill(c, src);
    let Some(g) = group_of_unit(c, target.id) else { return Err(0) };
    let pvp_or_gvg = || mapflag(c, target.map, MF_PVP) || mapflag(c, target.map, MF_GVG);
    let breath_like = matches!(skill_id, RK_DRAGONBREATH | RK_DRAGONBREATH_WATER | NC_SELFDESTRUCTION | NC_AXETORNADO | SR_SKYNETBLOW);
    if skill_get_inf2(g.skill_id, &[INF2_ISTRAP]) && g.unit_id != consts::UNT_USED_TRAPS {
        if skill_id == 0 || g.skill_id == NPC_REVERBERATION || g.skill_id == WM_POEMOFNETHERWORLD {
        } else if skill_get_inf2(skill_id, &[INF2_TARGETTRAP]) {
            // Only a few skills can target traps
            if breath_like && !pvp_or_gvg() {
                // Can only hit traps in PVP/GVG maps
                return Err(0);
            }
        } else {
            return Err(0);
        }
        Ok(())
    } else if g.skill_id == WZ_ICEWALL || (g.skill_id == GN_WALLOFTHORN && skill_id != GN_CARTCANNON) {
        if breath_like {
            // Can only hit icewall in PVP/GVG maps
            if !pvp_or_gvg() {
                return Err(0);
            }
        } else if skill_id == HT_CLAYMORETRAP {
            // Can't hit icewall
            return Err(0);
        } else if flag & crate::battle::BCT_ALL == crate::battle::BCT_ALL && !skill_get_inf2(skill_id, &[INF2_TARGETTRAP]) {
            // Usually BCT_ALL stands for only hitting chars, but skills specifically set to hit traps also hit icewall
            return Err(-1);
        }
        Ok(())
    } else {
        //Excepting traps, Icewall, and Wall of Thorns, you should not be able to target skills.
        Err(0)
    }
}

/// battle.cpp battle_weapon_attack: a normal attack that damaged a skill unit (a Blast Mine is knocked back, a Wall of
/// Thorn cell loses a hit).
pub fn battle_damage_unit(c: &Ctx, src: &Unit, target: &Unit) {
    use crate::db::skills::*;
    let Some(su) = get_unit(c, target.id) else { return };
    let Some(g) = skill_id2group(c, su.group_id) else { return };
    if g.skill_id == HT_BLASTMINE {
        iface::skill_blown(c, src, target, 3, -1, consts::BLOWN_NONE);
    }
    if g.skill_id == GN_WALLOFTHORN {
        let mut left = 0;
        upd_unit(c, su.id(), |u| {
            u.val2 -= 1;
            left = u.val2;
        });
        if left <= 0 {
            skill_delunit(c, su.id());
        }
    }
}

/// battle.cpp battle_status_block_damage, SC_SAFETYWALL: the Safety Wall / Steinwand group of the status (val3) takes
/// a melee hit (one of its val2 hits and, RENEWAL, `damage` of its val3 HP); false when the group is gone (the caller
/// ends the status).
pub fn safetywall_block(c: &Ctx, sce_val2: i32, sce_val3: i32, damage: i64) -> bool {
    use crate::db::skills::*;
    let Some(group) = skill_id2group(c, sce_val3) else { return false };
    let skill = sce_val2 as u16;
    if skill == MG_SAFETYWALL || skill == MH_STEINWAND {
        let hits = group.val2 - 1;
        upd_group(c, group.group_id, |g| g.val2 = hits);
        if hits <= 0 {
            skill_delunitgroup(c, group.group_id);
        } else if group.val3 as i64 - damage > 0 {
            let d = damage.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
            upd_group(c, group.group_id, |g| g.val3 -= d);
        } else {
            skill_delunitgroup(c, group.group_id);
        }
    }
    true
}

/// battle.cpp is_infinite_defense: a Reverberation or Poem of the Netherworld unit takes no damage.
pub fn is_infinite_defense_unit(c: &Ctx, target: &Unit) -> bool {
    use crate::db::skills::*;
    group_of_unit(c, target.id).map(|g| g.skill_id == NPC_REVERBERATION || g.skill_id == WM_POEMOFNETHERWORLD).unwrap_or(false)
}

/// battle.cpp battle_attr_fix: fire damage to a Wall of Thorn turns it into a Fire Wall. Returns false when the
/// function ends there with 0 damage (the unit is gone, dead, or already turning).
pub fn attr_fix_wall_of_thorn(c: &Ctx, target: &Unit) -> bool {
    let Some(su) = get_unit(c, target.id) else { return false };
    if !su.alive {
        return false;
    }
    let Some(sg) = skill_id2group(c, su.group_id) else { return false };
    if sg.val3 == -1 {
        return false;
    }
    let Some(src2) = crate::unit::get(c, sg.src_id) else { return false };
    if crate::status::sc::status_isdead(c, &src2) {
        return false;
    }
    if sg.unit_id != consts::UNT_FIREWALL {
        let x = (sg.val3 >> 16) as i16;
        let y = (sg.val3 & 0xffff) as i16;
        setting::skill_unitsetting(c, &src2, sg.skill_id, sg.skill_lv, x, y, 1);
        upd_group(c, sg.group_id, |g| {
            g.val3 = -1;
            g.limit = c.tick() - g.tick + 300;
        });
    }
    true
}

/// battle.cpp battle_check_target, `case BL_SKILL` of the actual source: Some(return value) when the check ends
/// there (a unit that may / may not hit its own caster, a trap a Sight Blaster keeps from going off).
pub fn check_target_src_unit(c: &Ctx, src: &Unit, target: &Unit) -> Option<i32> {
    use crate::db::skills::*;
    let Some(g) = group_of_unit(c, src.id) else { return Some(0) };
    if g.src_id == target.id {
        if skill_get_inf2(g.skill_id, &[INF2_NOTARGETSELF]) {
            return Some(-1);
        }
        if skill_get_inf2(g.skill_id, &[INF2_TARGETSELF]) {
            return Some(1);
        }
    }
    //Status changes that prevent traps from triggering
    if skill_get_inf2(g.skill_id, &[INF2_ISTRAP]) && crate::battle::mobif::has_sc(target.kind) {
        if let Some(e) = crate::status::sc::get_sce(c, target.id, crate::status::consts::SC_SIGHTBLASTER) {
            if e.val2 > 0 && e.val4 % 2 == 0 {
                return Some(-1);
            }
        }
    }
    None
}

/// A world rebuild is a restart of rAthena: no unit, group, saved unit or skill_duration entry survives (the NPC files
/// set the map flags again). Runs before map_cell's reset, which clears the cells units set.
pub fn on_world_rebuilt(c: &Ctx) {
    for id in c.db().skill_unit().iter().map(|r| r.id).collect::<Vec<_>>() {
        c.db().skill_unit().id().delete(id);
        c.db().unit().id().delete(id);
        c.db().block_seq().id().delete(id);
        crate::map::block_seq_removed(id);
    }
    for g in c.db().skill_unit_group().iter().map(|g| g.group_id).collect::<Vec<_>>() {
        c.db().skill_unit_group().group_id().delete(g);
    }
    for k in c.db().map_skill_duration().iter().map(|r| r.key).collect::<Vec<_>>() {
        c.db().map_skill_duration().key().delete(k);
    }
    for m in c.db().map_skill_duration_order().iter().map(|r| r.map).collect::<Vec<_>>() {
        c.db().map_skill_duration_order().map().delete(m);
    }
    for k in c.db().skill_usave().iter().map(|r| r.char_id).collect::<Vec<_>>() {
        c.db().skill_usave().char_id().delete(k);
    }
    for k in c.db().skill_unit_tickset().iter().map(|r| r.bl_id).collect::<Vec<_>>() {
        c.db().skill_unit_tickset().bl_id().delete(k);
    }
    unitdb::clear(c);
    let mut s = state(c);
    s.db_free_lock = 0;
    put_state(c, &s);
}

use crate::map::block_seq as _;
use crate::unit::unit as _;
