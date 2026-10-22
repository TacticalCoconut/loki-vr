//! battle.cpp battle_get_master, battle_check_target, battle_check_range, battle_getcurrentskill and the status.cpp
//! getters they use (status_get_party_id, status_get_guild_id).

use super::mobif::{self, *};
use super::*;
use crate::map::{BL_CHAR, BL_ELEM, BL_HOM, BL_MER, BL_MOB, BL_NPC, BL_PC, BL_PET, BL_SKILL};
use crate::npc::{mapflag, npc as _, MF_BATTLEGROUND, MF_PVP};
use crate::status::consts::*;
use crate::status::defs::OPTION_INVISIBLE;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// map.hpp e_mapflag values the module's npc.rs does not name.
pub const MF_PVP_NOPARTY: i16 = 7;
pub const MF_PVP_NOGUILD: i16 = 8;
pub const MF_GVG_NOPARTY: i16 = 10;
pub const MF_PVP_NOCALCRANK: i16 = 50;

/// battle.cpp battle_getcurrentskill: the skill a unit is using (a skill unit: its group's skill).
pub fn battle_getcurrentskill(c: &Ctx, u: &Unit) -> u16 {
    if u.kind == BL_SKILL {
        return crate::skillunit::current_skill(c, u);
    }
    u.ud.skill_id
}

/// battle.cpp battle_get_master: the upmost master of a unit (the unit itself when it has none): pets (pet/data.rs),
/// homunculi (homunculus/data.rs), mercenaries and elementals (companion.rs), skill units (skillunit); monsters'
/// masters come from the mob agent's data.
pub fn battle_get_master(c: &Ctx, src: &Unit) -> Unit {
    let mut cur = src.clone();
    loop {
        let prev_id = cur.id;
        let next = match cur.kind {
            BL_MOB => {
                let m = mobif::master_id(c, &cur);
                if m != 0 {
                    unit::get(c, m)
                } else {
                    Some(cur.clone())
                }
            }
            BL_SKILL => match crate::skillunit::master_of(c, &cur) {
                Some(src) => unit::get(c, src),
                None => Some(cur.clone()),
            },
            // BL_PET: pd->master (pet/data.rs)
            BL_PET => match crate::pet::data::get(c, cur.id).and_then(|pd| unit::get(c, pd.master)) {
                Some(m) => Some(m),
                None => Some(cur.clone()),
            },
            // BL_HOM: hd->master (battle.cpp:7812-7815, homunculus/data.rs)
            BL_HOM => match crate::homunculus::data::get(c, cur.id).filter(|hd| crate::pc::get(c, hd.master).is_some()).and_then(|hd| unit::get(c, hd.master)) {
                Some(m) => Some(m),
                None => Some(cur.clone()),
            },
            // BL_MER / BL_ELEM: ->master (battle.cpp:7816-7823, companion.rs)
            BL_MER | BL_ELEM => match crate::companion::master_of(c, cur.id).and_then(|m| unit::get(c, m)) {
                Some(m) => Some(m),
                None => Some(cur.clone()),
            },
            _ => Some(cur.clone()),
        };
        match next {
            Some(n) if n.id != prev_id => cur = n,
            // map_id2bl failed (nullptr) or no master: the loop ends on `prev`
            _ => return cur,
        }
    }
}

/// status.cpp status_get_party_id.
pub fn status_get_party_id(c: &Ctx, bl: &Unit) -> i32 {
    match bl.kind {
        BL_PC => crate::party::party_id_of(c, bl.id) as i32,
        BL_MOB => {
            let m = mobif::master_id(c, bl);
            if m > 0 {
                if mobif::special_ai(c, bl) != AI_NONE {
                    if crate::pc::get(c, m).is_some() {
                        return crate::party::party_id_of(c, m) as i32;
                    }
                }
                return -(m as i32);
            }
            0
        }
        BL_SKILL => crate::skillunit::party_id(c, bl),
        // BL_PET: the master's party (pet/data.rs)
        BL_PET => crate::pet::data::get(c, bl.id).filter(|pd| crate::pc::get(c, pd.master).is_some()).map(|pd| crate::party::party_id_of(c, pd.master) as i32).unwrap_or(0),
        // BL_HOM: hd->master->status.party_id (homunculus/data.rs)
        BL_HOM => crate::homunculus::data::get(c, bl.id).filter(|hd| crate::pc::get(c, hd.master).is_some()).map(|hd| crate::party::party_id_of(c, hd.master) as i32).unwrap_or(0),
        // BL_MER / BL_ELEM: the master's party (status.cpp:9141-9152, companion.rs)
        BL_MER | BL_ELEM => crate::companion::master_of(c, bl.id).filter(|&m| crate::pc::get(c, m).is_some()).map(|m| crate::party::party_id_of(c, m) as i32).unwrap_or(0),
        _ => 0,
    }
}

/// status.cpp status_get_guild_id.
pub fn status_get_guild_id(c: &Ctx, bl: &Unit) -> i32 {
    match bl.kind {
        BL_PC => crate::pc::character_of(c, bl.id).map(|ch| ch.guild_id as i32).unwrap_or(0),
        BL_MOB => {
            if let Some(g) = mobif::guardian_guild_id(c, bl) {
                return g; // Guardian's guild [Skotlex]
            }
            if mobif::special_ai(c, bl) != AI_NONE {
                let m = mobif::master_id(c, bl);
                if crate::pc::get(c, m).is_some() {
                    return crate::pc::character_of(c, m).map(|ch| ch.guild_id as i32).unwrap_or(0); // Alchemist's mobs
                }
            }
            0
        }
        BL_SKILL => crate::skillunit::guild_id(c, bl),
        // BL_PET: the master's guild (pet/data.rs)
        BL_PET => crate::pet::data::get(c, bl.id).and_then(|pd| crate::pc::character_of(c, pd.master)).map(|ch| ch.guild_id as i32).unwrap_or(0),
        // BL_HOM: hd->master->status.guild_id (homunculus/data.rs)
        BL_HOM => crate::homunculus::data::get(c, bl.id).filter(|hd| crate::pc::get(c, hd.master).is_some()).and_then(|hd| crate::pc::character_of(c, hd.master)).map(|ch| ch.guild_id as i32).unwrap_or(0),
        // BL_MER / BL_ELEM: the master's guild (status.cpp:9187-9190, 9199-9202, companion.rs)
        BL_MER | BL_ELEM => crate::companion::master_of(c, bl.id).and_then(|m| crate::pc::character_of(c, m)).map(|ch| ch.guild_id as i32).unwrap_or(0),
        BL_NPC => {
            // nd->u.scr.guild_id of a script NPC (flagemblem: woe.rs)
            if c.db().npc().id().find(bl.id).map(|nd| nd.subtype == crate::npc::NPCTYPE_SCRIPT).unwrap_or(false) {
                return crate::woe::npc_guild_id(c, bl.id);
            }
            0
        }
        _ => 0,
    }
}

/// status.cpp status_get_emblem_id.
pub fn status_get_emblem_id(c: &Ctx, bl: &Unit) -> i32 {
    match bl.kind {
        BL_PC => bl.emblem_id, // sd->guild_emblem_id (the unit row's copy)
        BL_PET => crate::pet::data::get(c, bl.id).and_then(|pd| crate::unit::get(c, pd.master)).map(|u| u.emblem_id).unwrap_or(0), // the master's guild_emblem_id
        // BL_HOM: hd->master->guild_emblem_id (status.cpp:9232-9235, homunculus/status.rs)
        BL_HOM => crate::homunculus::status::status_get_emblem_id_hom(c, bl.id),
        BL_MOB => {
            if let Some(gd) = crate::woe::guardian::get(c, bl.id) {
                return gd.emblem_id; // Guardian's guild [Skotlex]
            }
            if mobif::special_ai(c, bl) != AI_NONE {
                let m = mobif::master_id(c, bl);
                if crate::pc::get(c, m).is_some() {
                    return crate::unit::get(c, m).map(|u| u.emblem_id).unwrap_or(0); // Alchemist's mobs [Skotlex]
                }
            }
            0
        }
        BL_NPC => {
            // a script NPC with a guild (flagemblem) shows that guild's current emblem
            let script = c.db().npc().id().find(bl.id).map(|nd| nd.subtype == crate::npc::NPCTYPE_SCRIPT).unwrap_or(false);
            let gid = crate::woe::npc_guild_id(c, bl.id);
            if script && gid > 0 {
                if let Some(g) = crate::guild::guild_search(c, gid) {
                    return g.guild.emblem_id;
                }
            }
            0
        }
        // BL_MER / BL_ELEM: the master's guild_emblem_id (status.cpp:9235-9238, 9246-9249, companion.rs)
        BL_MER | BL_ELEM => crate::companion::master_of(c, bl.id).filter(|&m| crate::pc::get(c, m).is_some()).and_then(|m| crate::unit::get(c, m)).map(|u| u.emblem_id).unwrap_or(0),
        _ => 0,
    }
}

/// guild.cpp guild_isallied (the guild agent's port).
fn guild_isallied(c: &Ctx, guild_id: i32, guild_id2: i32) -> bool {
    crate::guild::guild_isallied(c, guild_id, guild_id2)
}

/// battleground.cpp bg_team_get_id (battleground.rs).
fn bg_team_get_id(c: &Ctx, bl: &Unit) -> i32 {
    crate::battleground::bg_team_get_id(c, bl)
}

/// battle.cpp battle_get_exception_ai.
fn battle_get_exception_ai(c: &Ctx, src: &Unit) -> bool {
    if src.kind != BL_MOB {
        return false;
    }
    matches!(mobif::special_ai(c, src), AI_ABR | AI_ATTACK | AI_BIONIC | AI_ZANZOU)
}

/// map.hpp mapdata_flag_vs (the agit flags of castles are read through npc.rs).
fn map_flag_vs(c: &Ctx, m: u16) -> bool {
    crate::npc::mapflag_vs(c, m)
}

/// skill.hpp e_skill_unit_id UNT_USED_TRAPS (UNT_SAFETYWALL 0x7e + 14).
pub const UNT_USED_TRAPS: u16 = 0x8c;

/// map.hpp mapdata_flag_gvg.
fn map_flag_gvg(c: &Ctx, m: u16) -> bool {
    crate::npc::mapflag_gvg(c, m)
}

/// battle.cpp battle_check_target: the relation between two units. 1: `flag` holds (enemy, party, …); -1: it does
/// not; 0: never a valid target.
pub fn battle_check_target(c: &Ctx, src: &Unit, target: &Unit, flag: i32) -> i32 {
    let mut state = 0;
    let mut strip_enemy = true; // whether to remove BCT_ENEMY when it is also friend / ally
    let m = target.map;
    // t_bl / s_bl hold the 'master' of the attack, while src / target are the actual objects involved.
    let t_bl = battle_get_master(c, target);
    let s_bl = battle_get_master(c, src);
    // Can't hit self and master, but can hit other slaves
    if flag & BCT_WOS != 0 && (src.id == target.id || s_bl.id == target.id) {
        return -1;
    }
    if s_bl.kind == BL_PC {
        match t_bl.kind {
            BL_MOB => {
                if crate::pc::has_permission(c, s_bl.id, crate::rules::PC_PERM_DISABLE_PVM) {
                    return 0;
                }
            }
            BL_PC => {
                if crate::pc::has_permission(c, s_bl.id, crate::rules::PC_PERM_DISABLE_PVP) {
                    return 0;
                }
            }
            _ => {}
        }
    }
    // Checks on actual target
    match target.kind {
        BL_PC => {
            let invincible = crate::pc::get(c, target.id).map(|p| p.invincible_timer != 0).unwrap_or(false);
            if invincible || target.sc.option & OPTION_INVISIBLE != 0 {
                return -1; // Cannot be targeted yet.
            }
            if has_sc(src.kind) {
                if let Some(e) = crate::status::sc::get_sce(c, src.id, SC_VOICEOFSIREN) {
                    if e.val2 == target.id as i32 {
                        return -1;
                    }
                }
            }
        }
        BL_MOB => {
            if target.ud.immune_attack {
                return 0;
            }
            let ai = mobif::special_ai(c, target);
            if (((ai == AI_SPHERE && conf::alchemist_summon_setting() & 2 != 0) // Marine Spheres
                || (ai == AI_FLORA && conf::alchemist_summon_setting() & 1 != 0))
                && s_bl.kind == BL_PC
                && src.kind != BL_MOB) // Floras
                || (ai == AI_ZANZOU && t_bl.id != s_bl.id) // Zanzou
                || (ai == AI_FAW && (t_bl.id != s_bl.id || (s_bl.kind == BL_PC && src.kind != BL_MOB)))
            {
                // Targettable by players
                state |= BCT_ENEMY;
                strip_enemy = false;
            }
        }
        BL_SKILL => {
            // Traps, Ice Wall and Wall of Thorn: nothing else of this kind can be targeted (skillunit's port).
            if let Err(r) = crate::skillunit::check_target_unit(c, src, target, flag) {
                return r;
            }
            state |= BCT_ENEMY;
            strip_enemy = false;
        }
        BL_MER | BL_HOM | BL_ELEM => {
            if target.ud.immune_attack {
                return 0;
            }
        }
        // All else not specified is an invalid target.
        _ => return 0,
    }
    // Checks on target master
    match t_bl.kind {
        BL_PC => {
            if t_bl.id != s_bl.id {
                let blocked = crate::pc::get(c, t_bl.id).map(|p| p.block_action & crate::pc::PCBLOCK_IMMUNE != 0).unwrap_or(false);
                if (blocked || (crate::status::sc::has_sce(c, t_bl.id, SC_KINGS_GRACE) && s_bl.kind != BL_PC)) && flag & BCT_ENEMY != 0 {
                    return 0; // Global immunity only to Attacks
                }
                let t_karma = crate::pc::character_of(c, t_bl.id).map(|ch| ch.karma != 0).unwrap_or(false);
                let s_karma = s_bl.kind == BL_PC && crate::pc::character_of(c, s_bl.id).map(|ch| ch.karma != 0).unwrap_or(false);
                if t_karma && s_karma {
                    state |= BCT_ENEMY; // Characters with bad karma may fight amongst them
                }
                if state::get(c, t_bl.id).killable {
                    state |= BCT_ENEMY; // Everything can kill it
                    strip_enemy = false;
                }
            }
        }
        BL_MOB => {
            if mobif::guardian_guild_id(c, &t_bl).map(|g| g != 0).unwrap_or(false) && !map_flag_gvg(c, m) {
                return 0; // Disable guardians/emperiums owned by Guilds on non-woe times.
            }
        }
        _ => {}
    }
    // Checks on actual src type (BL_SKILL: the trap rules, skillunit's port)
    match src.kind {
        BL_PET => {
            if t_bl.kind != BL_MOB && flag & BCT_ENEMY != 0 {
                return 0; // Pet may not attack non-mobs.
            }
            if t_bl.kind == BL_MOB && flag & BCT_ENEMY != 0 && (mobif::guardian_guild_id(c, &t_bl).is_some() || mobif::special_ai(c, &t_bl) == AI_GUILD) {
                return 0; // pet may not attack Guardians/Emperium
            }
        }
        BL_SKILL => {
            if let Some(r) = crate::skillunit::check_target_src_unit(c, src, target) {
                return r;
            }
        }
        BL_MER => {
            if t_bl.kind == BL_MOB && mobif::mob_id(c, &t_bl) == MOBID_EMPERIUM && flag & BCT_ENEMY != 0 {
                return 0; // mercenary may not attack Emperium
            }
        }
        _ => {}
    }
    // Checks on source master
    match s_bl.kind {
        BL_PC => {
            if s_bl.id != t_bl.id {
                if state::get(c, s_bl.id).killer {
                    state |= BCT_ENEMY; // Can kill anything
                    strip_enemy = false;
                } else if let Some(r) = crate::duel::battle_check_target(c, s_bl.id, &t_bl, m, BCT_ENEMY & flag != 0) {
                    // in a duel: duel targets can ONLY be your enemy, nothing else; nothing out of the duel (duel.rs)
                    return r;
                }
            }
            let s_guild = crate::pc::character_of(c, s_bl.id).map(|ch| ch.guild_id).unwrap_or(0);
            if s_guild == 0 && t_bl.kind == BL_MOB && mobif::mob_id(c, &t_bl) == MOBID_EMPERIUM && map_flag_gvg(c, m) {
                return 0; // If you don't belong to a guild, can't target emperium.
            }
            if t_bl.kind != BL_PC {
                state |= BCT_ENEMY; // Natural enemy.
            }
        }
        BL_MOB => {
            if mobif::guardian_guild_id(c, &s_bl).map(|g| g != 0).unwrap_or(false) && !map_flag_gvg(c, m) {
                return 0; // Disable guardians/emperium owned by Guilds on non-woe times.
            }
            if mobif::special_ai(c, &s_bl) == AI_NONE {
                // Normal mobs
                let t_ai = if t_bl.kind == BL_MOB { mobif::special_ai(c, &t_bl) } else { AI_NONE };
                if (target.kind == BL_MOB && t_bl.kind == BL_PC && !battle_get_exception_ai(c, target))
                    || (t_bl.kind == BL_MOB && (t_ai == AI_NONE || t_ai == AI_WAVEMODE))
                {
                    state |= BCT_PARTY; // Normal mobs with no ai or with AI_WAVEMODE are friends.
                } else {
                    state |= BCT_ENEMY; // However, all else are enemies.
                }
            } else if t_bl.kind == BL_MOB && mobif::special_ai(c, &t_bl) == AI_NONE {
                state |= BCT_ENEMY; // Natural enemy for AI mobs are normal mobs.
            }
        }
        _ => {
            // Need some sort of default behaviour for unhandled types.
            if t_bl.kind != s_bl.kind {
                state |= BCT_ENEMY;
            }
        }
    }
    if flag & BCT_ALL == BCT_ALL {
        // All actually stands for all attackable chars, icewall and traps
        return if target.kind & (BL_CHAR | BL_SKILL) != 0 { 1 } else { -1 };
    }
    if flag == BCT_NOONE {
        return -1;
    }
    if t_bl.id == s_bl.id {
        // No need for further testing.
        state |= BCT_SELF | BCT_PARTY | BCT_GUILD;
        if state & BCT_ENEMY != 0 && strip_enemy {
            state &= !BCT_ENEMY;
        }
        return if flag & state != 0 { 1 } else { -1 };
    }
    if map_flag_vs(c, m) {
        // Check rivalry settings.
        let (mut sbg_id, mut tbg_id) = (0, 0);
        let bg = mapflag(c, m, MF_BATTLEGROUND);
        if bg {
            sbg_id = bg_team_get_id(c, &s_bl);
            tbg_id = bg_team_get_id(c, &t_bl);
        }
        let pvp = mapflag(c, m, MF_PVP);
        let gvg = map_flag_gvg(c, m);
        if flag & (BCT_PARTY | BCT_ENEMY) != 0 {
            let s_party = status_get_party_id(c, &s_bl);
            if s_party != 0
                && s_party == status_get_party_id(c, &t_bl)
                && !(pvp && mapflag(c, m, MF_PVP_NOPARTY))
                && !(gvg && mapflag(c, m, MF_GVG_NOPARTY))
                && (!bg || sbg_id == tbg_id)
            {
                state |= BCT_PARTY;
            } else {
                state |= BCT_ENEMY;
            }
        }
        if flag & (BCT_GUILD | BCT_ENEMY) != 0 {
            let s_guild = status_get_guild_id(c, &s_bl);
            let t_guild = status_get_guild_id(c, &t_bl);
            if !(pvp && mapflag(c, m, MF_PVP_NOGUILD))
                && s_guild != 0
                && t_guild != 0
                && (s_guild == t_guild || (flag & BCT_SAMEGUILD == 0 && guild_isallied(c, s_guild, t_guild)))
                && (!bg || sbg_id == tbg_id)
            {
                state |= BCT_GUILD;
            } else {
                state |= BCT_ENEMY;
            }
        }
        if state & BCT_ENEMY != 0 && bg && sbg_id != 0 && sbg_id == tbg_id {
            state &= !BCT_ENEMY;
        }
        if state & BCT_ENEMY != 0 && crate::conf::pk_mode() != 0 && !gvg && s_bl.kind == BL_PC && t_bl.kind == BL_PC {
            // Prevent novice engagement on pk_mode (feature by Valaris)
            let lv = |id| crate::pc::character_of(c, id).map(|ch| ch.base_level as i32).unwrap_or(0);
            let novice = |id| crate::status::data::sd_get(c, id).map(|s| s.class_ & crate::db::jobs::MAPID_SECONDMASK == crate::db::jobs::MAPID_NOVICE).unwrap_or(false);
            let (l1, l2) = (lv(s_bl.id), lv(t_bl.id));
            if novice(s_bl.id)
                || novice(t_bl.id)
                || l1 < conf::pk_min_level()
                || l2 < conf::pk_min_level()
                || (conf::pk_level_range() != 0 && (l1 - l2).abs() > conf::pk_level_range())
            {
                state &= !BCT_ENEMY;
            }
        }
    } else {
        // Non pvp/gvg, check party/guild settings.
        if flag & BCT_PARTY != 0 || state & BCT_ENEMY != 0 {
            let s_party = status_get_party_id(c, &s_bl);
            if s_party != 0 && s_party == status_get_party_id(c, &t_bl) {
                state |= BCT_PARTY;
            }
        }
        if flag & BCT_GUILD != 0 || state & BCT_ENEMY != 0 {
            let s_guild = status_get_guild_id(c, &s_bl);
            let t_guild = status_get_guild_id(c, &t_bl);
            if s_guild != 0 && t_guild != 0 && (s_guild == t_guild || (flag & BCT_SAMEGUILD == 0 && guild_isallied(c, s_guild, t_guild))) {
                state |= BCT_GUILD;
            }
        }
    }
    if state == 0 {
        // If not an enemy, nor a guild, nor party, nor yourself, it's neutral.
        state = BCT_NEUTRAL;
    } else if state & BCT_ENEMY != 0 && strip_enemy && state & (BCT_SELF | BCT_PARTY | BCT_GUILD) != 0 {
        // Alliance state takes precedence over enemy one.
        state &= !BCT_ENEMY;
    }
    if flag & state != 0 {
        1
    } else {
        -1
    }
}

/// battle.cpp battle_check_range: in range and a straight shot (CIRCULAR_AREA is off: players use the client's
/// round distance).
pub fn battle_check_range(c: &Ctx, src: &Unit, bl: &Unit, range: i32) -> bool {
    let _ = c;
    if src.map != bl.map {
        return false;
    }
    let (dx, dy) = ((src.x - bl.x) as i32, (src.y - bl.y) as i32);
    if src.kind == BL_PC {
        // Range for players' attacks and skills should always have a circular check. [Angezerus]
        if !crate::path::check_distance_client(dx, dy, range) {
            return false;
        }
    } else if !crate::path::check_distance(dx, dy, range) {
        return false;
    }
    let d = crate::path::distance(dx, dy);
    if d < 2 {
        return true; // No need for path checking.
    }
    if d > crate::conf::area_size() as i32 {
        return false; // Avoid targetting objects beyond your range of sight.
    }
    crate::path::path_search_long(src.map, src.x, src.y, bl.x, bl.y, crate::map::CellChk::ChkWall)
}


/// battle.cpp MAX_ENEMY_SEARCH_COUNT.
pub const MAX_ENEMY_SEARCH_COUNT: usize = 30;

/// battle.cpp battle_gettargeted: a random unit within AREA_SIZE that targets (attack or skill) `target`.
pub fn battle_gettargeted(c: &Ctx, target: &Unit) -> Option<Unit> {
    let r = crate::conf::area_size();
    let mut list = Vec::new();
    for bl in crate::map::units_in_area(c, target.map, target.x - r, target.y - r, target.x + r, target.y + r, BL_CHAR) {
        // battle_gettargeted_sub
        if bl.id == target.id || list.len() >= MAX_ENEMY_SEARCH_COUNT {
            continue;
        }
        if bl.ud.target == target.id || bl.ud.skilltarget == target.id {
            list.push(bl);
        }
    }
    if list.is_empty() {
        return None;
    }
    let i = c.rnd() as usize % list.len();
    Some(list.swap_remove(i))
}

/// battle.cpp battle_gettarget: the id a unit is targeting (monsters: md->target_id, mob agent; pets: pet agent).
pub fn battle_gettarget(c: &Ctx, bl: &Unit) -> u32 {
    match bl.kind {
        BL_PC | BL_HOM | BL_MER | BL_ELEM => bl.ud.target,
        BL_MOB => mobif::target_id(c, bl),
        // BL_PET: pd->target_id (pet/data.rs)
        BL_PET => crate::pet::data::get(c, bl.id).map(|pd| pd.target_id).unwrap_or(0),
        _ => 0,
    }
}

/// battle.cpp battle_getenemy: a random visible, living enemy of `target` within `range` among the unit kinds `type_`.
pub fn battle_getenemy(c: &Ctx, target: &Unit, type_: u16, range: i32) -> Option<Unit> {
    let r = range as i16;
    let mut list = Vec::new();
    for bl in crate::map::units_in_area(c, target.map, target.x - r, target.y - r, target.x + r, target.y + r, type_) {
        // battle_getenemy_sub
        if bl.id == target.id || list.len() >= MAX_ENEMY_SEARCH_COUNT {
            continue;
        }
        if crate::status::sc::status_isdead(c, &bl) {
            continue;
        }
        if !mobif::status_check_visibility(c, target, &bl, false) {
            continue;
        }
        if battle_check_target(c, target, &bl, BCT_ENEMY) > 0 {
            list.push(bl);
        }
    }
    if list.is_empty() {
        return None;
    }
    let i = c.rnd() as usize % list.len();
    Some(list.swap_remove(i))
}

/// battle.cpp battle_getenemyarea: a random living enemy of `src` in the square around (x, y), `ignore_id` excluded.
pub fn battle_getenemyarea(c: &Ctx, src: &Unit, x: i16, y: i16, range: i16, type_: u16, ignore_id: u32) -> Option<Unit> {
    let mut list = Vec::new();
    for bl in crate::map::units_in_area(c, src.map, x - range, y - range, x + range, y + range, type_) {
        // battle_getenemyarea_sub: ignores the caster and a possible pre-target
        if bl.id == src.id || bl.id == ignore_id || list.len() >= MAX_ENEMY_SEARCH_COUNT {
            continue;
        }
        if crate::status::sc::status_isdead(c, &bl) {
            continue;
        }
        if battle_check_target(c, src, &bl, BCT_ENEMY) > 0 {
            list.push(bl);
        }
    }
    if list.is_empty() {
        return None;
    }
    let i = c.rnd() as usize % list.len();
    Some(list.swap_remove(i))
}
