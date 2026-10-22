//! map/npc.cpp: NPCs, warps, shops, monster spawn lines and map flags, loaded from the NPC script files the module
//! embeds (npc_parsesrcfile and the npc_parse_* functions), plus OnTouch areas (npc_touch_area_allnpc).
//!
//! The world — NPC units, their data, map flags and spawn lines — is built from the embedded files when the
//! database is first published and again whenever a published module carries different data (`world_state`).
//! Script bodies are kept as source spans; the script engine compiles them (phase 5).

use std::collections::HashMap;

use spacetimedb::{table, SpacetimeType, Table};

use crate::clif;
use crate::data;
use crate::map::{self, CellChk, BL_NPC};
use crate::unit::{unit, ScView, Unit, UnitData, ViewData, OPTION_CLOAK, OPTION_HIDE};
use crate::world::Ctx;
use crate::cstr::Scan;

/// enum npc_subtype (map.hpp).
pub const NPCTYPE_WARP: u8 = 0;
pub const NPCTYPE_SHOP: u8 = 1;
pub const NPCTYPE_SCRIPT: u8 = 2;
pub const NPCTYPE_CASHSHOP: u8 = 3;
pub const NPCTYPE_ITEMSHOP: u8 = 4;
pub const NPCTYPE_POINTSHOP: u8 = 5;
pub const NPCTYPE_TOMB: u8 = 6;
pub const NPCTYPE_MARKETSHOP: u8 = 7;
pub const NPCTYPE_BARTER: u8 = 8;

/// e_npcv_status (npc.hpp).
pub const NPCVIEW_DISABLE: u8 = 0x01;
pub const NPCVIEW_ENABLE: u8 = 0x02;
pub const NPCVIEW_HIDEON: u8 = 0x08;
pub const NPCVIEW_CLOAKON: u8 = 0x20;

/// npc.hpp START_NPC_NUM.
pub const START_NPC_NUM: u32 = 110_000_000;
/// JT_* view classes used here (npc.hpp).
pub const JT_FAKENPC: i32 = -1;
pub const JT_WARPNPC: i32 = 45;
pub const JT_HIDDEN_WARP_NPC: i32 = 139;
pub const JT_GUILD_FLAG: i32 = 722;
pub const JT_INVISIBLE: i32 = 32767;
/// mmo.hpp DEFAULT_NPC_WALK_SPEED, NPC_NAME_LENGTH.
pub const DEFAULT_NPC_WALK_SPEED: u16 = 200;
pub const NPC_NAME_LENGTH: usize = 50;

// e_mapflag values (script_constants.hpp → consts.bin; fixed here for the flags the code tests).
pub const MF_NOMEMO: i16 = 0;
pub const MF_NOTELEPORT: i16 = 1;
pub const MF_NOSAVE: i16 = 2;
pub const MF_NOPENALTY: i16 = 4;
pub const MF_PVP: i16 = 6;
pub const MF_NOSKILL: i16 = 12;
pub const MF_NOWARP: i16 = 13;
pub const MF_NOGO: i16 = 22;
pub const MF_NORETURN: i16 = 33;
pub const MF_NOWARPTO: i16 = 34;
pub const MF_GVG: i16 = 9;
pub const MF_PARTYLOCK: i16 = 14;
pub const MF_SNOW: i16 = 16;
pub const MF_FOG: i16 = 17;
pub const MF_SAKURA: i16 = 18;
pub const MF_LEAVES: i16 = 19;
pub const MF_CLOUDS: i16 = 23;
pub const MF_CLOUDS2: i16 = 24;
pub const MF_FIREWORKS: i16 = 25;
pub const MF_GVG_CASTLE: i16 = 26;
pub const MF_GVG_DUNGEON: i16 = 27;
pub const MF_NOBASEEXP: i16 = 29;
pub const MF_NOJOBEXP: i16 = 30;
pub const MF_NOMOBLOOT: i16 = 31;
pub const MF_NOMVPLOOT: i16 = 32;
pub const MF_PVP_NIGHTMAREDROP: i16 = 35;
pub const MF_RESTRICTED: i16 = 36;
pub const MF_NOCOMMAND: i16 = 37;
pub const MF_JEXP: i16 = 39;
pub const MF_BEXP: i16 = 40;
pub const MF_NOEXPPENALTY: i16 = 44;
pub const MF_TOWN: i16 = 46;
pub const MF_BATTLEGROUND: i16 = 51;
pub const MF_NOUSECART: i16 = 54;
pub const MF_NOSUNMOONSTARMIRACLE: i16 = 56;
pub const MF_FORCEMINEFFECT: i16 = 57;
pub const MF_NOLOCKON: i16 = 58;
pub const MF_SKILL_DAMAGE: i16 = 60;
pub const MF_NOCOSTUME: i16 = 61;
pub const MF_GVG_TE_CASTLE: i16 = 62;
pub const MF_GVG_TE: i16 = 63;
pub const MF_HIDEMOBHPBAR: i16 = 64;
pub const MF_NOLOOT: i16 = 65;
pub const MF_NOEXP: i16 = 66;
pub const MF_SKILL_DURATION: i16 = 69;
pub const MF_NOZENYPENALTY: i16 = 5;
pub const MF_SPECIALPOPUP: i16 = 78;
pub const MF_INVINCIBLE_TIME: i16 = 80;

/// One item a shop sells (npc_item_list).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct ShopItem {
    pub nameid: u32,
    pub value: i32,
    pub qty: i32,
    pub flag: u8,
}

/// The shops made by `duplicate` from one source shop (npc_parse_duplicate: `nd->u.shop.shop_item =
/// dnd->u.shop.shop_item`, npc.cpp:4615-4616): they all point at the source's item list. Rebuilt with the world.
#[table(accessor = npc_shop_family)]
pub struct NpcShopFamily {
    #[primary_key]
    pub root: u32,
    /// The source and its duplicates.
    pub members: Vec<u32>,
}

/// The other shops that still share `nd`'s item list. The list is one allocation in rAthena: a change made in place
/// (npcshopupdate, a market sale, npc_market_checkall / npcshopadditem on an existing market item) is seen by every
/// shop that points at it. npcshopitem / npcshopadditem / npcshopdelitem RECREATE the list of the shop they name,
/// which gets a list of its own here (rAthena's other shops keep the old pointer and count: after a realloc that moved
/// or shrank the block they read freed memory, which is not reproduced). `before` is `nd`'s list before the change:
/// the shops whose list still equals it are the ones that share it.
pub fn shop_sharers(c: &Ctx, nd: &Npc, before: &[ShopItem]) -> Vec<Npc> {
    let root = if nd.src_id != 0 { nd.src_id } else { nd.id };
    let Some(f) = c.db().npc_shop_family().root().find(root) else { return Vec::new() };
    f.members.iter().filter(|&&id| id != nd.id).filter_map(|&id| c.db().npc().id().find(id)).filter(|n| n.shop == before).collect()
}

/// Gives the shops that shared `nd`'s list (`before`, see shop_sharers) the list `nd` has now.
pub fn shop_share(c: &Ctx, nd: &Npc, before: &[ShopItem]) {
    if nd.shop == before {
        return;
    }
    for mut n in shop_sharers(c, nd, before) {
        n.shop = nd.shop.clone();
        c.db().npc().id().update(n);
    }
}

/// npc_data: everything but position / look, which are the NPC's unit row.
#[table(accessor = npc, index(accessor = by_map, btree(columns = [map])))]
#[derive(Clone, Debug)]
pub struct Npc {
    #[primary_key]
    pub id: u32,
    #[unique]
    pub exname: String,
    pub name: String,
    pub subtype: u8,
    /// Map id, or -1 for a floating NPC.
    pub map: i32,
    pub x: i16,
    pub y: i16,
    pub class: i32,
    /// The NPC a duplicate was made from (nd->src_id).
    pub src_id: u32,
    /// OnTouch / warp trigger area half-sizes; -1 = none.
    pub xs: i16,
    pub ys: i16,
    pub warp_map: i32,
    pub warp_x: i16,
    pub warp_y: i16,
    pub trigger_on_hidden: bool,
    pub state: u8,
    pub is_invisible: bool,
    pub path: String,
    /// Script NPCs and duplicates: where the script body is (index into the embedded files, byte span, line).
    pub script_file: u32,
    pub script_start: u32,
    pub script_end: u32,
    pub script_line: u32,
    pub shop: Vec<ShopItem>,
    pub shop_discount: bool,
    pub itemshop_nameid: u32,
    pub pointshop_str: String,
    pub owner_char: u32,
    /// Order among the map's touch NPCs: warps first, then scripts (map.cpp map_addnpc grouping).
    pub touch_order: u32,
}

/// User functions (`function<TAB>script<TAB>Name<TAB>{...}`): source spans for the script engine.
#[table(accessor = npc_function)]
#[derive(Clone, Debug)]
pub struct NpcFunction {
    #[primary_key]
    pub name: String,
    pub script_file: u32,
    pub script_start: u32,
    pub script_end: u32,
    pub script_line: u32,
}

/// map_data flags (map.hpp getMapFlag / setMapFlag). key = map << 16 | flag.
#[table(accessor = mapflag)]
#[derive(Clone, Debug)]
pub struct MapFlag {
    #[primary_key]
    pub key: u32,
    pub map: u16,
    pub flag: i16,
    pub value: i32,
}

/// The nosave map flag's return point (mapdata->save); save_map -1 = the character's save point.
#[table(accessor = map_save_point)]
#[derive(Clone, Debug)]
pub struct MapSavePoint {
    #[primary_key]
    pub map: u16,
    pub save_map: i32,
    pub x: i16,
    pub y: i16,
}

/// struct spawn_data: a `monster` line (the monsters themselves come with phase 3).
#[table(accessor = spawn_data, index(accessor = by_map, btree(columns = [map])))]
#[derive(Clone, Debug)]
pub struct SpawnData {
    #[primary_key]
    #[auto_inc]
    pub id: u32,
    pub mob_id: i32,
    pub map: u16,
    pub x: i16,
    pub y: i16,
    pub xs: i16,
    pub ys: i16,
    pub num: u16,
    pub active: u16,
    pub delay1: u32,
    pub delay2: u32,
    pub level: u32,
    pub size: u8,
    pub ai: u8,
    pub boss: bool,
    pub dynamic: bool,
    pub name: String,
    pub eventname: String,
    pub path: String,
}

/// Which data the static world was last built from (data::data_version).
#[table(accessor = world_state)]
pub struct WorldState {
    #[primary_key]
    pub key: u8,
    pub data_version: u64,
    pub npc_count: u32,
    pub built_at_tick: i64,
}

// ---------------------------------------------------------------------------------------------- map flags

fn mf_key(m: u16, flag: i16) -> u32 {
    ((m as u32) << 16) | (flag as u16 as u32)
}

/// map_getmapflag: the flag's value (BEXP / JEXP default to 100, map_flags_init).
pub fn mapflag_value(c: &Ctx, m: u16, flag: i16) -> i32 {
    match flag {
        MF_NOLOOT => return (mapflag(c, m, MF_NOMOBLOOT) && mapflag(c, m, MF_NOMVPLOOT)) as i32,
        MF_NOPENALTY => return (mapflag(c, m, MF_NOEXPPENALTY) && mapflag(c, m, MF_NOZENYPENALTY)) as i32,
        MF_NOEXP => return (mapflag(c, m, MF_NOBASEEXP) && mapflag(c, m, MF_NOJOBEXP)) as i32,
        _ => {}
    }
    match c.db().mapflag().key().find(mf_key(m, flag)) {
        Some(f) => f.value,
        None if flag == MF_BEXP || flag == MF_JEXP => 100,
        None => 0,
    }
}

pub fn mapflag(c: &Ctx, m: u16, flag: i16) -> bool {
    mapflag_value(c, m, flag) != 0
}

fn set_flag_raw(c: &Ctx, m: u16, flag: i16, value: i32) {
    let key = mf_key(m, flag);
    if value == 0 || ((flag == MF_BEXP || flag == MF_JEXP) && value == 100) {
        c.db().mapflag().key().delete(key);
        if flag == MF_BEXP || flag == MF_JEXP {
            if value == 0 {
                c.db().mapflag().insert(MapFlag { key, map: m, flag, value: 0 });
            }
        }
        return;
    }
    if c.db().mapflag().key().find(key).is_some() {
        c.db().mapflag().key().update(MapFlag { key, map: m, flag, value });
    } else {
        c.db().mapflag().insert(MapFlag { key, map: m, flag, value });
    }
}

/// map.hpp mapdata_flag_vs.
pub fn mapflag_vs(c: &Ctx, m: u16) -> bool {
    let agit = crate::guild::agit_flag(c) || crate::guild::agit2_flag(c);
    mapflag(c, m, MF_PVP)
        || mapflag(c, m, MF_GVG_DUNGEON)
        || mapflag(c, m, MF_GVG)
        || (agit && mapflag(c, m, MF_GVG_CASTLE))
        || mapflag(c, m, MF_GVG_TE)
        || (crate::guild::agit3_flag(c) && mapflag(c, m, MF_GVG_TE_CASTLE))
        || mapflag(c, m, MF_BATTLEGROUND)
}

/// map.hpp mapdata_flag_gvg.
pub fn mapflag_gvg(c: &Ctx, m: u16) -> bool {
    let agit = crate::guild::agit_flag(c) || crate::guild::agit2_flag(c);
    mapflag(c, m, MF_GVG) || (agit && mapflag(c, m, MF_GVG_CASTLE)) || mapflag(c, m, MF_GVG_TE) || (crate::guild::agit3_flag(c) && mapflag(c, m, MF_GVG_TE_CASTLE))
}

/// map.hpp mapdata_flag_gvg2.
pub fn mapflag_gvg2(c: &Ctx, m: u16) -> bool {
    mapflag(c, m, MF_GVG) || mapflag(c, m, MF_GVG_TE) || mapflag(c, m, MF_GVG_CASTLE) || mapflag(c, m, MF_GVG_TE_CASTLE)
}

/// The nosave flag's return point: Some((map, x, y)) for a fixed point, None for "SavePoint".
pub fn mapflag_nosave_point(c: &Ctx, m: u16) -> Option<(String, u16, u16)> {
    let sp = c.db().map_save_point().map().find(m)?;
    if sp.save_map < 0 {
        return None;
    }
    let name = data::maps().get(sp.save_map as u16)?.name.clone();
    Some((name, sp.x as u16, sp.y as u16))
}

/// map.cpp map_setmapflag_sub: sets a flag with rAthena's conflict rules (PvP / GvG / battleground exclude each
/// other; nopenalty / noexp / noloot set their pairs). `args` is the flag value for the valued flags.
pub fn setmapflag(c: &Ctx, m: u16, flag: i16, status: bool, arg: i32) {
    let on = |f| mapflag(c, m, f);
    match flag {
        MF_PVP => {
            set_flag_raw(c, m, MF_PVP, status as i32);
            if status {
                for f in [MF_GVG, MF_GVG_TE, MF_GVG_DUNGEON, MF_GVG_CASTLE, MF_GVG_TE_CASTLE, MF_BATTLEGROUND] {
                    if on(f) {
                        set_flag_raw(c, m, f, 0);
                    }
                }
            }
            // players on the map are told the new property (clif_map_property_mapall) when a script changes it (phase 5)
        }
        MF_GVG | MF_GVG_TE => {
            set_flag_raw(c, m, flag, status as i32);
            if status {
                if on(MF_PVP) {
                    set_flag_raw(c, m, MF_PVP, 0);
                }
                if on(MF_BATTLEGROUND) {
                    set_flag_raw(c, m, MF_BATTLEGROUND, 0);
                }
            }
        }
        MF_GVG_CASTLE | MF_GVG_TE_CASTLE => {
            if status {
                if flag == MF_GVG_CASTLE && on(MF_GVG_TE_CASTLE) {
                    set_flag_raw(c, m, MF_GVG_TE_CASTLE, 0);
                }
                if flag == MF_GVG_TE_CASTLE && on(MF_GVG_CASTLE) {
                    set_flag_raw(c, m, MF_GVG_CASTLE, 0);
                }
                if on(MF_PVP) {
                    set_flag_raw(c, m, MF_PVP, 0);
                }
            }
            set_flag_raw(c, m, flag, status as i32);
        }
        MF_GVG_DUNGEON => {
            if status && on(MF_PVP) {
                set_flag_raw(c, m, MF_PVP, 0);
            }
            set_flag_raw(c, m, flag, status as i32);
        }
        MF_NOBASEEXP | MF_NOJOBEXP => {
            if status {
                if flag == MF_NOBASEEXP && mapflag_value(c, m, MF_BEXP) != 100 {
                    set_flag_raw(c, m, MF_BEXP, 0);
                }
                if flag == MF_NOJOBEXP && mapflag_value(c, m, MF_JEXP) != 100 {
                    set_flag_raw(c, m, MF_JEXP, 0);
                }
            }
            set_flag_raw(c, m, flag, status as i32);
        }
        MF_RESTRICTED => {
            // zone bits: mapdata->zone |= (1 << (zone + 1)) << 3
            let zone_key = mf_key(m, MF_RESTRICTED);
            let cur = c.db().mapflag().key().find(zone_key).map(|f| f.value).unwrap_or(0);
            let bit = (1i32 << (arg + 1)) << 3;
            let next = if status { cur | bit } else { cur ^ bit };
            set_flag_raw(c, m, MF_RESTRICTED, next);
        }
        MF_NOCOMMAND => set_flag_raw(c, m, flag, if status { if arg <= 0 { 100 } else { arg } } else { 0 }),
        MF_JEXP | MF_BEXP => {
            if status {
                if flag == MF_JEXP && on(MF_NOJOBEXP) {
                    set_flag_raw(c, m, MF_NOJOBEXP, 0);
                }
                if flag == MF_BEXP && on(MF_NOBASEEXP) {
                    set_flag_raw(c, m, MF_NOBASEEXP, 0);
                }
                set_flag_raw(c, m, flag, arg);
            } else {
                set_flag_raw(c, m, flag, 0);
            }
        }
        MF_SPECIALPOPUP | MF_INVINCIBLE_TIME => set_flag_raw(c, m, flag, if status { arg } else { 0 }),
        MF_BATTLEGROUND => {
            if status {
                for f in [MF_PVP, MF_GVG, MF_GVG_DUNGEON, MF_GVG_CASTLE] {
                    if on(f) {
                        set_flag_raw(c, m, f, 0);
                    }
                }
                set_flag_raw(c, m, flag, if arg <= 0 || arg > 2 { 1 } else { arg });
            } else {
                set_flag_raw(c, m, flag, 0);
            }
        }
        MF_NOLOOT => {
            set_flag_raw(c, m, MF_NOMOBLOOT, status as i32);
            set_flag_raw(c, m, MF_NOMVPLOOT, status as i32);
        }
        MF_NOPENALTY => {
            set_flag_raw(c, m, MF_NOEXPPENALTY, status as i32);
            set_flag_raw(c, m, MF_NOZENYPENALTY, status as i32);
        }
        MF_NOEXP => {
            set_flag_raw(c, m, MF_NOBASEEXP, status as i32);
            set_flag_raw(c, m, MF_NOJOBEXP, status as i32);
        }
        _ => set_flag_raw(c, m, flag, status as i32),
    }
}

// ---------------------------------------------------------------------------------------------- world build

/// Builds the static world if the embedded data changed since it was last built (or it never was).
pub fn ensure_world(c: &Ctx) {
    // (the release switch decides which NPC files are loaded, so a change of mode builds the world again: release.rs)
    let want = data::data_version() ^ crate::release::world_key();
    if let Some(ws) = c.db().world_state().key().find(0) {
        if ws.data_version == want {
            if crate::battle_config::get(crate::battle_config::Bc::AREA_SIZE) == 14 {
                crate::battle_config::battle_set_value(c, "area_size", "60");
            }
            return;
        }
    }
    build_world(c, want);
}

/// The items the NPC files' shops sell (npc_parse_shop sets item_data::maxchance to -1 for them when no monster
/// drops them). Rebuilt with the world.
#[table(accessor = npc_shop_sold)]
pub struct NpcShopSold {
    #[primary_key]
    pub nameid: u32,
}

/// item_data::maxchance: the highest drop rate a monster has for the item (mob.cpp mob_drop_ratio_adjust), else -1
/// when a shop of the NPC files sells it, else 0.
pub fn item_maxchance(c: &Ctx, nameid: u32) -> i32 {
    match crate::db::mobs::mob_item_drop_info().find(nameid).map(|d| d.maxchance).unwrap_or(0) {
        0 if c.db().npc_shop_sold().nameid().find(nameid).is_some() => -1,
        v => v,
    }
}

/// Tears down the old static world (NPC units and rows, map flags, spawn lines, functions) and parses every NPC file.
fn build_world(c: &Ctx, version: u64) {
    // the map server's shutdown: the characters without a client (autotraders) leave, their shops kept for the
    // restore below (autotrade.rs)
    crate::autotrade::world_shutdown(c);
    // a rebuilt world is a map-server restart: no instance survives it (instance.rs; do_final_instance)
    crate::instance::on_world_rebuild(c);
    // … and do_init reads the battle conf again (map.cpp do_init: battle_config_read(BATTLE_CONF_FILENAME)): what
    // battle_set_value changed is gone, the host's import file is read again (battle_config/mod.rs)
    crate::battle_config::battle_config_read(c);
    // … then mob_db (do_init_mob): the monsters' EXP is rated with the conf just read
    crate::battle_config::mob_load(c);
    // do_init_npc / npc_reload: the market stock is read before the NPC files load (and write their own)
    let markets = crate::shop::npc_market_fromsql(c);
    for nameid in c.db().npc_shop_sold().iter().map(|r| r.nameid).collect::<Vec<_>>() {
        c.db().npc_shop_sold().nameid().delete(nameid);
    }
    for root in c.db().npc_shop_family().iter().map(|r| r.root).collect::<Vec<_>>() {
        c.db().npc_shop_family().root().delete(root);
    }
    // npc_unload of every NPC: a waiting room goes with its NPC, its users kicked (npc.cpp:3486-3487
    // chat_deletenpcchat); the rooms' OnInit `waitingroom` would otherwise find the old room
    for owner in crate::chat::npcs_with_chat(c) {
        crate::chat::chat_deletenpcchat(c, owner);
    }
    // channel_db starts again from conf/channels.conf (social W12)
    crate::channel::reset_for_world_build(c);
    let old_npcs: Vec<u32> = c.db().npc().iter().map(|n| n.id).collect();
    for id in &old_npcs {
        c.db().unit().id().delete(*id);
        c.db().npc().id().delete(*id);
    }
    let flags: Vec<u32> = c.db().mapflag().iter().map(|f| f.key).collect();
    for k in flags {
        c.db().mapflag().key().delete(k);
    }
    let saves: Vec<u16> = c.db().map_save_point().iter().map(|s| s.map).collect();
    for k in saves {
        c.db().map_save_point().map().delete(k);
    }
    // npc_reload: every monster is freed and the maps' dynamic lists are dropped (the monster system)
    crate::mob::spawn::on_world_rebuild(c);
    // castle_db's run-time data, the guild flag cache and the agit flags start over (woe.rs; the guardians went with the
    // monsters)
    crate::woe::on_world_rebuild(c);
    let spawns: Vec<u32> = c.db().spawn_data().iter().map(|s| s.id).collect();
    for k in spawns {
        c.db().spawn_data().id().delete(k);
    }
    let funcs: Vec<String> = c.db().npc_function().iter().map(|f| f.name).collect();
    for k in funcs {
        c.db().npc_function().name().delete(k);
    }
    // nd->qi_data / mapdata->qi_npc (questinfo fills them again in OnInit)
    crate::quest::clear_questinfo(c);
    // the NPCs' script2 fields (status, progress bars, hat effects) start zeroed like a new npc_data
    crate::pc_misc::on_world_rebuilt(c, &old_npcs);
    // skill units, their groups and mapdata->skill_duration start over (skillunit)
    crate::skillunit::on_world_rebuilt(c);
    // mapdata->damage_adjust / skill_damage start over too (map_skill_damage.rs)
    crate::map_skill_damage::on_world_rebuilt(c);
    // map_data cells and iwall_db start over (map_setcell / setwall changes of the previous world are gone)
    crate::map_cell::on_world_rebuilt(c);
    // map.cpp do_init: do_init_battleground (teams, queues, reservations, the xy timer) runs before do_init_npc
    crate::battleground::do_init_battleground(c);
    // userfunc_db and the compiled scripts start over; compile errors are reported while the files load
    crate::script::code::clear_functions(c);
    crate::script::code::set_loading(true);
    // map_reloadnpc(false): npc_src_files is the start-up set; the run-time loads' state starts over (npc_load.rs)
    crate::npc_load::on_world_build(c);
    let files = data::npc_files();
    let boot = data::npc_boot_count().min(files.len());
    let mut b = Builder::new(c, Mode::Boot);
    for (i, f) in files[..boot].iter().enumerate() {
        if crate::release::skips_npc_file(&f.path) {
            continue; // Loki's test NPC files (npc/custom/) are not part of a release server's world
        }
        b.parse_file(i as u32, &f.text, &f.path);
    }
    b.finish_touch_order();
    let count = b.by_exname.len() as u32;
    log::info!(
        "Done loading '{}' NPCs: {} warps, {} shops, {} scripts, {} spawn lines, {} map flags, {} functions ({} files)",
        count, b.stats.warps, b.stats.shops, b.stats.scripts, b.stats.mobs, b.stats.mapflags, b.stats.functions, boot
    );
    // stylist_db.load(); barter_db.load(): BarterDatabase::loadingFinished creates the barter NPCs (the first load's
    // order)
    b.create_barters(crate::db::items::barter::barter_db().iter());
    // npc_market_checkall: after npc_read_event_script, before the fake NPC and OnInit (do_init_npc's order; a
    // world rebuild is a restart with new files, there is no @reloadscript)
    crate::shop::npc_market_checkall(c, &markets);
    let row = WorldState { key: 0, data_version: version, npc_count: count, built_at_tick: c.tick() };
    if c.db().world_state().key().find(0).is_some() {
        c.db().world_state().key().update(row);
    } else {
        c.db().world_state().insert(row);
    }
    // the script engine: fake NPC, event export, NPC timers, OnInit / OnInterIfInit(Once)
    let next_id = b.next_id;
    crate::script::npc_run::on_world_built(c, next_id);
    // chrif_on_ready once the char server connects after the start: the autotraders are loaded again (autotrade.rs)
    crate::autotrade::chrif_on_ready(c);
}

#[derive(Default)]
struct Stats {
    warps: u32,
    shops: u32,
    scripts: u32,
    mobs: u32,
    mapflags: u32,
    functions: u32,
}

/// When a [`Builder`] runs, which decides how what it parses joins the world. The parsing itself (npc_parsesrcfile and
/// the npc_parse_* functions) is the same in every mode.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub(crate) enum Mode {
    /// do_init_npc (build_world): an empty world and nobody on the maps. NPC ids count from START_NPC_NUM here, the
    /// units are placed without packets (and with their state's option directly), npcname_db is the builder's own
    /// map, the trigger-area order is kept in memory and written when every file is loaded, and the script labels are
    /// exported after the load (script::npc_run::on_world_built).
    Boot,
    /// npc_reload (@reloadscript, npc_load.rs): every NPC was unloaded first, players may be on the maps. NPC ids come
    /// from the NPC id counter (npc_get_new_npc_id, which npc_reload does not reset), npcname_db is the `npc` table,
    /// NPCs appear with clif_spawn when their map has players and take their state with npc_enable_target, the map
    /// flags are set with their run-time effects (map_setmapflag_sub). The maps hold no NPC when the load starts, so
    /// the trigger-area order is kept in memory as at start-up; the labels are exported after the load, into the
    /// emptied ev_db, as the start-up does.
    Reload,
    /// npc_addsrcfile at run time (@loadnpc / @reloadnpcfile) and npc_duplicate_npc (the `duplicate` commands): as
    /// Reload, but the NPCs join maps that already have NPCs (map_addnpc on the live trigger-area order) and each
    /// script NPC's labels go into ev_db as it is made (npc_parse_script / npc_parse_duplicate's label loop).
    Live,
}

pub(crate) struct Builder<'a, 'b> {
    c: &'a Ctx<'b>,
    mode: Mode,
    next_id: u32,
    /// npcname_db (exname → id, subtype) at start-up; the NPCs made by this builder otherwise.
    by_exname: HashMap<String, (u32, u8)>,
    /// map_addnpc's trigger-area section of each map's mapdata->npc (ids in array order) and its npc_num_warp.
    touch_order: HashMap<u16, (Vec<u32>, usize)>,
    stats: Stats,
    /// npc_parse_duplicate's `owner` (npc_duplicate_npc_for_player): the char id a dynamic NPC belongs to, 0 = none.
    owner_char: u32,
    /// The NPCs this builder made, in the order it made them.
    made: Vec<u32>,
}

/// C isspace in the "C" locale.
fn is_space(b: u8) -> bool {
    matches!(b, b' ' | b'\t' | b'\n' | 0x0b | 0x0c | b'\r')
}

/// script.cpp skip_space: whitespace, // and /* */ comments.
pub fn skip_space(t: &[u8], mut p: usize) -> usize {
    loop {
        while p < t.len() && is_space(t[p]) {
            p += 1;
        }
        if p + 1 < t.len() && t[p] == b'/' && t[p + 1] == b'/' {
            while p < t.len() && t[p] != b'\n' {
                p += 1;
            }
        } else if p + 1 < t.len() && t[p] == b'/' && t[p + 1] == b'*' {
            p += 2;
            loop {
                if p >= t.len() {
                    return p;
                }
                if p + 1 < t.len() && t[p] == b'*' && t[p + 1] == b'/' {
                    p += 2;
                    break;
                }
                p += 1;
            }
        } else {
            break;
        }
    }
    p
}

fn find(t: &[u8], from: usize, needle: &[u8]) -> Option<usize> {
    if from >= t.len() {
        return None;
    }
    t[from..].windows(needle.len()).position(|w| w == needle).map(|i| from + i)
}

fn find_byte(t: &[u8], from: usize, b: u8) -> Option<usize> {
    if from >= t.len() {
        return None;
    }
    t[from..].iter().position(|&c| c == b).map(|i| from + i)
}

/// strlib.cpp strline: 1-based line of a byte offset.
fn line_of(t: &[u8], pos: usize) -> u32 {
    1 + t[..pos.min(t.len())].iter().filter(|&&c| c == b'\n').count() as u32
}

/// npc.cpp npc_skip_script: from the first '{' at or after `start`, the offset just past its matching '}'.
fn skip_script(t: &[u8], start: usize, path: &str) -> Option<usize> {
    let mut p = find_byte(t, start, b'{')?;
    let mut curly = 1;
    while curly > 0 {
        p = skip_space(t, p + 1);
        if p >= t.len() {
            log::error!("Missing {curly} right curlys at file '{path}', line '{}'.", line_of(t, p));
            return None;
        }
        match t[p] {
            b'}' => curly -= 1,
            b'{' => curly += 1,
            b'"' => {
                p += 1;
                while p < t.len() && t[p] != b'"' {
                    if t[p] == b'\\' && t[p - 1] <= 0x7e {
                        p += 1;
                    } else if t[p] == b'\n' {
                        log::error!("{path}: Unexpected newline at string (line {}).", line_of(t, p));
                        return None;
                    }
                    p += 1;
                }
                if p >= t.len() {
                    log::error!("{path}: Unexpected end of string.");
                    return None;
                }
            }
            0 => return None,
            _ => {}
        }
    }
    Some(p + 1)
}

/// strlib.cpp sv_parse over one line with '\t', SV_TERMINATE_LF|SV_TERMINATE_CRLF (no escapes): the field
/// spans, the line end, and the field count.
fn sv_parse_line(t: &[u8], start: usize) -> (Vec<(usize, usize)>, usize) {
    let mut fields = Vec::new();
    let mut i = start;
    loop {
        let fs = i;
        while i < t.len() && t[i] != b'\t' && t[i] != b'\n' && !(t[i] == b'\r' && i + 1 < t.len() && t[i + 1] == b'\n') {
            i += 1;
        }
        fields.push((fs, i));
        if i >= t.len() {
            break;
        }
        if t[i] == b'\t' {
            i += 1;
            if i >= t.len() {
                // a trailing delimiter still ends the field list (sv.done once IS_END)
                break;
            }
            continue;
        }
        break; // line terminator
    }
    (fields, i)
}

/// A tiny sscanf for rAthena's `%Ns[^,]` / `%Nhd` / `%Nd` / `%Nu` field patterns.
/// `%15[^,],%6hd,%6hd,%4hd` → (map, x, y, dir) with the count of fields read.
fn scan_placement(w1: &[u8]) -> (usize, String, i16, i16, i16) {
    let mut s = Scan::new(w1);
    let Some(m) = s.set(15, b",") else { return (0, String::new(), 0, 0, 0) };
    if !s.lit(b',') {
        return (1, m, 0, 0, 0);
    }
    let Some(x) = s.int(6) else { return (1, m, 0, 0, 0) };
    if !s.lit(b',') {
        return (2, m, x as i16, 0, 0);
    }
    let Some(y) = s.int(6) else { return (2, m, x as i16, 0, 0) };
    if !s.lit(b',') {
        return (3, m, x as i16, y as i16, 0);
    }
    let Some(d) = s.int(4) else { return (3, m, x as i16, y as i16, 0) };
    (4, m, x as i16, y as i16, d as i16)
}

fn text(b: &[u8]) -> String {
    crate::net::wire::from_latin1(b)
}

/// strlib.cpp trim: both ends.
fn trim(b: &[u8]) -> &[u8] {
    let mut s = 0;
    let mut e = b.len();
    while s < e && is_space(b[s]) {
        s += 1;
    }
    while e > s && is_space(b[e - 1]) {
        e -= 1;
    }
    &b[s..e]
}

impl<'a, 'b> Builder<'a, 'b> {
    pub(crate) fn new(c: &'a Ctx<'b>, mode: Mode) -> Self {
        Builder { c, mode, next_id: START_NPC_NUM, by_exname: HashMap::new(), touch_order: HashMap::new(), stats: Default::default(), owner_char: 0, made: Vec::new() }
    }

    /// npc_parse_duplicate's owner (npc_duplicate_npc_for_player): the NPCs made belong to this character.
    pub(crate) fn set_owner(&mut self, char_id: u32) {
        self.owner_char = char_id;
    }

    /// The "Done loading" counters (npc_loadsrcfiles' summary): (warps, shops, scripts, spawn lines).
    pub(crate) fn totals(&self) -> (u32, u32, u32, u32) {
        (self.stats.warps, self.stats.shops, self.stats.scripts, self.stats.mobs)
    }

    /// npc_create_npc's npc_get_new_npc_id: the builder's own count at start-up, the NPC id counter at run time.
    fn new_id(&mut self) -> u32 {
        if self.mode != Mode::Boot {
            return crate::mob::spawn::npc_get_new_npc_id(self.c);
        }
        let id = self.next_id;
        self.next_id += 1;
        id
    }

    /// npc_name2id(exname) != nullptr: npcname_db is the builder's map at start-up (it holds every NPC then), the `npc`
    /// table at run time.
    fn name_taken(&self, exname: &str) -> bool {
        if self.mode == Mode::Boot {
            return self.by_exname.contains_key(exname);
        }
        self.c.db().npc().exname().find(&exname.to_string()).is_some()
    }

    /// npc_name2id(srcname) for npc_parse_duplicate: the source NPC's row.
    fn find_by_exname(&self, exname: &str) -> Option<Npc> {
        if self.mode == Mode::Boot {
            let &(id, _) = self.by_exname.get(exname)?;
            return self.c.db().npc().id().find(id);
        }
        self.c.db().npc().exname().find(&exname.to_string())
    }

    /// npc.cpp npc_parsename: display name and unique name (`Name::Unique`), made unique when taken.
    fn parse_name(&self, w3: &[u8], m: i32, x: i16, y: i16, path: &str) -> (String, String) {
        let raw = text(w3);
        let (mut name, mut exname) = match raw.find("::") {
            Some(i) => (raw[..i].chars().take(NPC_NAME_LENGTH).collect::<String>(), raw[i + 2..].chars().take(NPC_NAME_LENGTH).collect::<String>()),
            None => {
                let n: String = raw.chars().take(NPC_NAME_LENGTH).collect();
                (n.clone(), n)
            }
        };
        if exname.is_empty() || exname.contains("::") {
            let nn = format!("0_{m}_{x}_{y}");
            log::warn!("npc_parsename: Invalid unique name in file '{path}'. Renaming '{exname}' to '{nn}'.");
            exname = nn;
        }
        if self.name_taken(&exname) {
            let mut i = 0;
            let nn = loop {
                i += 1;
                let nn = format!("{i}_{m}_{x}_{y}");
                if !self.name_taken(&nn) {
                    break nn;
                }
            };
            log::warn!("npc_parsename: Duplicate unique name in file '{path}'. Renaming '{exname}' to '{nn}'.");
            exname = nn;
        }
        if name.len() > NPC_NAME_LENGTH {
            name.truncate(NPC_NAME_LENGTH);
        }
        (name, exname)
    }

    /// npc.cpp npc_parseview: a number, a script constant, or a monster AegisName; JT_INVISIBLE otherwise.
    fn parse_view(&self, w4: &[u8], path: &str) -> i32 {
        let end = w4.iter().position(|&c| is_space(c) || c == b'/' || c == b',').unwrap_or(w4.len());
        let s = text(&w4[..end]);
        if let Some(v) = parse_c_long(&s) {
            return v as i32;
        }
        if let Some(v) = data::constant(&s) {
            return v as i32;
        }
        if let Some(id) = data::mobdb_search_aegisname(&s) {
            return id as i32;
        }
        log::warn!("npc_parseview: Invalid NPC constant '{s}' specified in file '{path}'. Defaulting to INVISIBLE.");
        JT_INVISIBLE
    }

    fn map_id(&self, name: &str) -> i32 {
        data::maps().id_of(name).map(|m| m as i32).unwrap_or(-1)
    }

    /// map_addnpc: NPCs with a trigger area are grouped at the front of mapdata->npc, warps first
    /// (0 < npc_num_warp < npc_num_area < npc_num). A new warp takes the first script's slot and that script moves
    /// to the end of the area section; a new script is appended to it. Returns a provisional (non-zero) order; the
    /// final one — the index in the section — is written when every file is loaded.
    fn next_touch_order(&mut self, m: i32, id: u32, subtype: u8) -> u32 {
        if m < 0 {
            return 0;
        }
        let (area, num_warp) = self.touch_order.entry(m as u16).or_default();
        if subtype == NPCTYPE_WARP {
            // npc[npc_num] = npc[npc_num_area]; npc[npc_num_area] = npc[npc_num_warp]; npc[npc_num_warp] = nd;
            if *num_warp < area.len() {
                let moved = area[*num_warp];
                area.push(moved);
                area[*num_warp] = id;
            } else {
                area.push(id);
            }
            *num_warp += 1;
        } else {
            area.push(id);
        }
        area.len() as u32
    }

    /// Writes each trigger-area NPC's final position in its map's section (touch_order = index + 1).
    pub(crate) fn finish_touch_order(&mut self) {
        for (area, _) in self.touch_order.values() {
            for (i, &id) in area.iter().enumerate() {
                if let Some(nd) = self.c.db().npc().id().find(id) {
                    if nd.touch_order != i as u32 + 1 {
                        self.c.db().npc().id().update(Npc { touch_order: i as u32 + 1, ..nd });
                    }
                }
            }
        }
    }

    fn insert(&mut self, nd: Npc, dir: u8) {
        self.made.push(nd.id);
        if self.mode == Mode::Boot {
            self.insert_boot(nd, dir);
        } else {
            self.insert_rt(nd, dir);
        }
    }

    /// The npc_parse_* placement at run time (npc_parse_script / npc_parse_duplicate / npc_parse_warp / npc_parse_shop
    /// / BarterDatabase::loadingFinished): on a map, map_addnpc (the trigger-area order: npc_setcells' CELL_NPC is
    /// has_touch_area_at, read from it), unit_dataset (direction, the timers' ticks now), map_addblock,
    /// status_set_viewdata (JT_FAKENPC keeps npc_viewdb[0], JT_INVISIBLE) and clif_spawn when the map has players;
    /// floating, map_addiddb only; then strdb_put(npcname_db) — the `npc` row goes in first here, as clif_spawn reads
    /// it — and the NPC's state: `if (nd->state != NPCVIEW_ENABLE) npc_enable_target(*nd, 0, nd->state)`, which sends
    /// what that state changes (the option, or the vanish of a disabled NPC). A warp's order differs a little in
    /// rAthena (map_addblock before unit_dataset); nothing is sent between the two.
    fn insert_rt(&mut self, mut nd: Npc, dir: u8) {
        let c = self.c;
        let touch = (nd.subtype == NPCTYPE_WARP || nd.subtype == NPCTYPE_SCRIPT) && nd.xs >= 0 && nd.ys >= 0 && nd.map >= 0;
        let (id, m, x, y, class, subtype, state) = (nd.id, nd.map, nd.x, nd.y, nd.class, nd.subtype, nd.state);
        // Reload: the maps were emptied, their order is built in memory (finish_touch_order); Live: map_addnpc now
        nd.touch_order = if touch && self.mode == Mode::Reload { self.next_touch_order(m, id, subtype) } else { 0 };
        nd.is_invisible = false;
        // npc_parse_duplicate's owner: nd->dynamicnpc.owner_char_id, owner->npc_id_dynamic.push_back(nd->id)
        nd.owner_char = self.owner_char;
        if self.owner_char != 0 {
            crate::npc_load::owner_push(c, self.owner_char, id);
        }
        self.by_exname.insert(nd.exname.clone(), (id, subtype));
        let name = nd.name.clone();
        c.db().npc().insert(nd);
        if m >= 0 {
            if touch && self.mode == Mode::Live {
                crate::instance_npc::map_addnpc_area(c, m as u16, id, subtype);
            }
            let tick = c.tick();
            let mut u = Unit {
                id,
                kind: BL_NPC,
                map: m as u16,
                x,
                y,
                on_map: false,
                name,
                vd: npc_viewdata(class),
                ud: UnitData { dir, sx: 8, sy: 8, to_x: x, to_y: y, attackabletime: tick, canact_tick: tick, canmove_tick: tick, endure_tick: tick, ..Default::default() },
                sc: ScView::default(),
                speed: DEFAULT_NPC_WALK_SPEED,
                mode: crate::unit::MD_CANMOVE,
                level: 0,
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
                npc_owner_char: self.owner_char,
            };
            c.db().unit().insert(u.clone());
            if crate::unit::addblock(c, &mut u) && class != JT_FAKENPC && crate::mob::map_users(c, m as u16) > 0 {
                clif::spawn(c, &u, false);
            }
        }
        if state != NPCVIEW_ENABLE {
            crate::script::buildins::npcctl::npc_enable_target(c, id, 0, state);
        }
    }

    /// The start-up placement (see [`Mode::Boot`]).
    fn insert_boot(&mut self, mut nd: Npc, dir: u8) {
        let touch = (nd.subtype == NPCTYPE_WARP || nd.subtype == NPCTYPE_SCRIPT) && nd.xs >= 0 && nd.ys >= 0 && nd.map >= 0;
        nd.touch_order = if touch { self.next_touch_order(nd.map, nd.id, nd.subtype) } else { 0 };
        self.by_exname.insert(nd.exname.clone(), (nd.id, nd.subtype));
        if nd.map >= 0 {
            let md = data::maps().get(nd.map as u16);
            let in_bounds = md.map(|md| nd.x >= 0 && nd.x < md.xs && nd.y >= 0 && nd.y < md.ys).unwrap_or(false);
            if in_bounds {
                let vd = npc_viewdata(nd.class);
                let mut option = 0;
                // npc_enable_target at load: the w2 state
                let mut invisible = false;
                match nd.state {
                    NPCVIEW_CLOAKON => option |= OPTION_CLOAK,
                    NPCVIEW_HIDEON => option |= OPTION_HIDE,
                    NPCVIEW_DISABLE => {
                        option |= OPTION_HIDE;
                        invisible = true;
                    }
                    _ => {}
                }
                nd.is_invisible = invisible;
                let u = Unit {
                    id: nd.id,
                    kind: BL_NPC,
                    map: nd.map as u16,
                    x: nd.x,
                    y: nd.y,
                    on_map: true,
                    name: nd.name.clone(),
                    vd,
                    ud: UnitData { dir, sx: 8, sy: 8, to_x: nd.x, to_y: nd.y, ..Default::default() },
                    sc: ScView { option, ..Default::default() },
                    speed: DEFAULT_NPC_WALK_SPEED,
                    mode: crate::unit::MD_CANMOVE,
                    level: 0,
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
                    invisible,
                    npc_owner_char: nd.owner_char,
                };
                self.c.db().unit().insert(u);
                crate::map::block_added(self.c, nd.id); // map_addblock in load order

            } else {
                log::warn!("npc: '{}' at {},{} is outside its map — not placed", nd.exname, nd.x, nd.y);
            }
        } else if nd.state == NPCVIEW_DISABLE {
            // npc_enable_target(nd, 0, NPCVIEW_DISABLE) on a floating NPC: is_invisible all the same
            nd.is_invisible = true;
        }
        self.c.db().npc().insert(nd);
    }

    /// npc.cpp BarterDatabase::loadingFinished: one NPC per barter (NPCTYPE_BARTER, named after the barter, on its
    /// map or floating), and the persisted stock of its stock-limited items (barter.rs). The database only lists
    /// the barters whose mechanics are enabled (feature.barter / feature.barter_extended). `order` is barter_db's
    /// iteration order (the first load's, or the one after a reload: npc_load.rs).
    pub(crate) fn create_barters<'d>(&mut self, order: impl Iterator<Item = &'d crate::db::items::barter::Barter>) {
        for barter in order {
            if !barter.npc {
                continue;
            }
            let m = barter.map.as_deref().map(|n| self.map_id(n)).unwrap_or(-1);
            let mut nd = self.blank(m, barter.x as i16, barter.y as i16, "npc/barters.yml");
            let (name, exname) = self.parse_name(barter.name.as_bytes(), m, barter.x as i16, barter.y as i16, "npc/barters.yml");
            nd.name = name;
            nd.exname = exname;
            nd.class = barter.sprite as i32;
            nd.subtype = NPCTYPE_BARTER;
            // Store the npcid for the destructor
            crate::npc_load::set_barter_npc(self.c, &barter.name, nd.id);
            self.insert(nd, barter.dir);
            crate::barter::load_stock(self.c, barter);
        }
    }

    fn blank(&mut self, m: i32, x: i16, y: i16, path: &str) -> Npc {
        let id = self.new_id();
        Self::blank_id(id, m, x, y, path)
    }

    /// npc_create_npc with an id already taken.
    fn blank_id(id: u32, m: i32, x: i16, y: i16, path: &str) -> Npc {
        Npc {
            id,
            exname: String::new(),
            name: String::new(),
            subtype: NPCTYPE_SCRIPT,
            map: m,
            x,
            y,
            class: JT_INVISIBLE,
            src_id: 0,
            xs: -1,
            ys: -1,
            warp_map: -1,
            warp_x: 0,
            warp_y: 0,
            trigger_on_hidden: false,
            state: NPCVIEW_ENABLE,
            is_invisible: false,
            path: path.to_string(),
            script_file: 0,
            script_start: 0,
            script_end: 0,
            script_line: 0,
            shop: Vec::new(),
            shop_discount: false,
            itemshop_nameid: 0,
            pointshop_str: String::new(),
            owner_char: 0,
            touch_order: 0,
        }
    }

    /// npc.cpp npc_parsesrcfile over the text of a file (the buffer), `path` being the name it was loaded by (nd->path,
    /// the messages). `file_idx` is where the script compiler finds the text (script/code.rs: an embedded file's index,
    /// or a run-time load's own index, npc_load.rs). False only for a file starting with a UTF-8 BOM (return 0); the
    /// parse errors stop or skip as rAthena's do and still return 1.
    pub(crate) fn parse_file(&mut self, file_idx: u32, t: &[u8], path: &str) -> bool {
        if t.len() >= 3 && t[0] == 0xEF && t[1] == 0xBB && t[2] == 0xBF {
            log::error!("npc_parsesrcfile: Detected unsupported UTF-8 BOM in file '{path}'. Stopping");
            return false;
        }
        let mut p = skip_space(t, 0);
        while p < t.len() {
            let (fields, line_end) = sv_parse_line(t, p);
            let count = fields.len();
            let fld = |i: usize| -> &[u8] { fields.get(i).map(|&(s, e)| &t[s..e]).unwrap_or(&[]) };
            let w1 = fld(0);
            let w2 = fld(1);
            let w3 = fld(2);
            let w4: &[u8] = if count > 3 { &t[fields[3].0..line_end] } else { &[] };
            if count < 3 {
                log::error!("npc_parsesrcfile: Unknown syntax in file '{path}', line '{}'. Stopping...", line_of(t, p));
                break;
            }
            let has_script = count > 3 && w2.len() >= 6 && w2[..6].eq_ignore_ascii_case(b"script");
            let next_line = |from: usize| find_byte(t, from, b'\n');
            if w1 != b"-" && !w1.eq_ignore_ascii_case(b"function") {
                let (n, mapname, x, y, _) = scan_placement(w1);
                let (x, y) = if n < 3 { (0, 0) } else { (x, y) };
                let skip_line = |this: &mut Self, p: usize| -> Option<usize> {
                    let q = if has_script { skip_script(t, p, path)? } else { p };
                    let _ = this;
                    next_line(q)
                };
                if n < 1 {
                    log::error!("npc_parsesrcfile: Invalid script definition in file '{path}', line '{}'. Skipping line...", line_of(t, p));
                    match skip_line(self, p) {
                        Some(q) => {
                            p = skip_space(t, q);
                            continue;
                        }
                        None => break,
                    }
                }
                let m = self.map_id(&mapname);
                if m < 0 {
                    // an unknown map (mapindex_name2id fails) or one this server does not load: skip
                    match skip_line(self, p) {
                        Some(q) => {
                            p = skip_space(t, q);
                            continue;
                        }
                        None => break,
                    }
                }
                let md = data::maps().get(m as u16).unwrap();
                if x < 0 || x >= md.xs || y < 0 || y >= md.ys {
                    log::error!("npc_parsesrcfile: Unknown coordinates ('{x}', '{y}') for map '{mapname}' in file '{path}', line '{}'. Skipping line...", line_of(t, p));
                    match skip_line(self, p) {
                        Some(q) => {
                            p = skip_space(t, q);
                            continue;
                        }
                        None => break,
                    }
                }
            }
            let w2s = text(w2).to_ascii_lowercase();
            let next: Option<usize> = if (w2s.starts_with("warp") || w2s.starts_with("warp2")) && count > 3 {
                self.parse_warp(w1, w2, w3, w4, p, t, path)
            } else if matches!(w2s.as_str(), "shop" | "cashshop" | "itemshop" | "pointshop" | "marketshop") && count > 3 {
                self.parse_shop(w1, &w2s, w3, w4, p, t, path)
            } else if has_script {
                if w1.eq_ignore_ascii_case(b"function") {
                    if w2s == "script" {
                        self.parse_function(w3, w4, p, t, path, file_idx)
                    } else {
                        log::error!("npc_parsesrcfile: Unable to parse, probably a missing or extra TAB in file '{path}', line '{}'. Skipping line...", line_of(t, p));
                        next_line(p)
                    }
                } else {
                    self.parse_script(w1, w2, w3, w4, p, t, path, file_idx)
                }
            } else if w2.starts_with(b"duplicate") && w2.get(9) == Some(&b'(') && count > 3 {
                // sscanf(w2, "duplicate%n"): the literal is matched case-sensitively (npc.cpp:5808)
                self.parse_duplicate(w1, w2, w3, w4, p, t, path)
            } else if (w2s == "monster" || w2s == "boss_monster") && count > 3 {
                self.parse_mob(w1, &w2s, w3, w4, p, t, path)
            } else if w2s == "mapflag" && count >= 3 {
                self.parse_mapflag(w1, trim(w3), trim(w4), p, t, path)
            } else {
                log::error!("npc_parsesrcfile: Unable to parse, probably a missing or extra TAB in file '{path}', line '{}'. Skipping line...", line_of(t, p));
                next_line(p)
            };
            match next {
                Some(q) => p = skip_space(t, q),
                None => break,
            }
        }
        true
    }

    /// npc.cpp npc_parse_warp.
    #[allow(clippy::too_many_arguments)]
    fn parse_warp(&mut self, w1: &[u8], w2: &[u8], w3: &[u8], w4: &[u8], start: usize, t: &[u8], path: &str) -> Option<usize> {
        let (n, mapname, x, y, _) = scan_placement(w1);
        let mut s = Scan::new(w4);
        let parsed = (|| {
            let xs = s.int(6)?;
            s.lit(b',').then_some(())?;
            let ys = s.int(6)?;
            s.lit(b',').then_some(())?;
            let to = s.set(15, b",")?;
            s.lit(b',').then_some(())?;
            let tx = s.int(6)?;
            s.lit(b',').then_some(())?;
            let ty = s.int(6)?;
            Some((xs as i16, ys as i16, to, tx as i16, ty as i16))
        })();
        let Some((xs, ys, to_map, to_x, to_y)) = parsed.filter(|_| n >= 3) else {
            log::error!("npc_parse_warp: Invalid warp definition in file '{path}', line '{}'.", line_of(t, start));
            return find_byte(t, start, b'\n');
        };
        let m = self.map_id(&mapname);
        let dest = self.map_id(&to_map);
        if dest < 0 {
            log::error!("npc_parse_warp: Unknown destination map in file '{path}', line '{}' : {to_map}", line_of(t, start));
            return find_byte(t, start, b'\n');
        }
        let mut nd = self.blank(m, x, y, path);
        let (name, exname) = self.parse_name(w3, m, x, y, path);
        nd.name = name;
        nd.exname = exname;
        let is_warp2 = w2.len() >= 5 && w2[..5].eq_ignore_ascii_case(b"warp2");
        nd.class = JT_WARPNPC; // warp_point_debug: no
        nd.subtype = NPCTYPE_WARP;
        nd.warp_map = dest;
        nd.warp_x = to_x;
        nd.warp_y = to_y;
        nd.xs = xs;
        nd.ys = ys;
        nd.trigger_on_hidden = is_warp2;
        nd.state = parse_state(w2, if is_warp2 { 6 } else { 5 }, path);
        self.stats.warps += 1;
        self.insert(nd, 0);
        find_byte(t, start, b'\n')
    }

    /// npc.cpp npc_parse_shop.
    #[allow(clippy::too_many_arguments)]
    fn parse_shop(&mut self, w1: &[u8], w2: &str, w3: &[u8], w4: &[u8], start: usize, t: &[u8], path: &str) -> Option<usize> {
        let (m, x, y, dir) = if w1 == b"-" {
            (-1, 0, 0, 0)
        } else {
            let (n, mapname, x, y, d) = scan_placement(w1);
            if n != 4 || !w4.contains(&b',') {
                log::error!("npc_parse_shop: Invalid shop definition in file '{path}', line '{}'.", line_of(t, start));
                return find_byte(t, start, b'\n');
            }
            (self.map_id(&mapname), x, y, d)
        };
        let subtype = match w2 {
            "cashshop" => NPCTYPE_CASHSHOP,
            "itemshop" => NPCTYPE_ITEMSHOP,
            "pointshop" => NPCTYPE_POINTSHOP,
            "marketshop" => NPCTYPE_MARKETSHOP,
            _ => NPCTYPE_SHOP,
        };
        let comma = w4.iter().position(|&c| c == b',')?;
        let mut p = comma; // points at ','
        let mut discount = 0;
        let mut itemshop_nameid = 0u32;
        let mut point_str = String::new();
        match subtype {
            NPCTYPE_ITEMSHOP => {
                let mut s = Scan::new(&w4[p..]);
                s.lit(b',');
                let Some(id) = s.int(10) else {
                    log::error!("npc_parse_shop: Invalid item cost definition in file '{path}', line '{}'.", line_of(t, start));
                    return find_byte(t, start, b'\n');
                };
                itemshop_nameid = id as u32;
                if s.lit(b':') {
                    discount = s.int(11).unwrap_or(0) as i32;
                }
                if crate::db::items::itemdb_exists(itemshop_nameid).is_none() {
                    log::warn!("npc_parse_shop: Invalid item ID cost in file '{path}', line '{}' (id '{itemshop_nameid}').", line_of(t, start));
                    return find_byte(t, start, b'\n'); // skip and continue
                }
                p = w4[p + 1..].iter().position(|&c| c == b',').map(|i| p + 1 + i).unwrap_or(w4.len());
            }
            NPCTYPE_POINTSHOP => {
                let mut s = Scan::new(&w4[p..]);
                s.lit(b',');
                let Some(ps) = s.set(31, b",:") else {
                    log::error!("npc_parse_shop: Invalid item cost definition in file '{path}', line '{}'.", line_of(t, start));
                    return find_byte(t, start, b'\n');
                };
                if s.lit(b':') {
                    discount = s.int(11).unwrap_or(0) as i32;
                }
                if matches!(ps.as_bytes()[0], b'$' | b'.' | b'\'') || ps.ends_with('$') {
                    log::warn!("npc_parse_shop: Invalid item cost variable type in file '{path}', line '{}'.", line_of(t, start));
                    return find_byte(t, start, b'\n');
                }
                point_str = ps;
                p = w4[p + 1..].iter().position(|&c| c == b',').map(|i| p + 1 + i).unwrap_or(w4.len());
            }
            NPCTYPE_MARKETSHOP => discount = 0,
            _ => {
                // ",<discount yes/no>:" or the first item
                let mut s = Scan::new(&w4[p..]);
                s.lit(b',');
                let first = s.set(31, b",:").unwrap_or_default();
                if s.lit(b':') && s.int(11).is_some() {
                    discount = 1; // an "id:price" pair: no discount flag given (sscanf == 2 → is_discount = 1)
                } else {
                    if first.eq_ignore_ascii_case("yes") {
                        discount = 1;
                    } else if first.eq_ignore_ascii_case("no") {
                        discount = 0;
                    } else {
                        log::error!("npc_parse_shop: unknown discount setting {first}");
                        return find_byte(t, start, b'\n');
                    }
                    p = w4[p + 1..].iter().position(|&c| c == b',').map(|i| p + 1 + i).unwrap_or(w4.len());
                }
            }
        }
        // nd = npc_create_npc(m, x, y) before the item loop (npc.cpp:4108): an empty shop has taken its NPC id
        let nd_id = self.new_id();
        let mut items: Vec<ShopItem> = Vec::new();
        while p < w4.len() {
            let mut s = Scan::new(&w4[p..]);
            s.lit(b',');
            let parsed = (|| {
                let id = s.int(10)?;
                s.lit(b':').then_some(())?;
                let v = s.int(11)?;
                if subtype == NPCTYPE_MARKETSHOP {
                    s.lit(b':').then_some(())?;
                    let q = s.int(11)?;
                    return Some((id, v, q));
                }
                Some((id, v, -1))
            })();
            let Some((id, value, qty)) = parsed else {
                log::error!("npc_parse_shop: Invalid item definition in file '{path}', line '{}'. Ignoring the rest of the line...", line_of(t, start));
                break;
            };
            let nameid2 = id as u32;
            let mut value = value as i32;
            let mut qty = qty as i32;
            let next_p = w4[p + 1..].iter().position(|&c| c == b',').map(|i| p + 1 + i).unwrap_or(w4.len());
            let Some(itd) = crate::db::items::itemdb_exists(nameid2) else {
                log::warn!("npc_parse_shop: Invalid sell item in file '{path}', line '{}' (id '{nameid2}').", line_of(t, start));
                p = next_p;
                continue;
            };
            if value < 0 {
                value = if subtype == NPCTYPE_SHOP || subtype == NPCTYPE_MARKETSHOP { itd.value_buy as i32 } else { 0 }; // Cashshop doesn't have a "buy price" in the item_db
            }
            if value == 0 && (subtype == NPCTYPE_SHOP || subtype == NPCTYPE_MARKETSHOP) {
                // NPC selling items for free!
                log::warn!("npc_parse_shop: Item {} [{nameid2}] is being sold for FREE in file '{path}', line '{}'.", itd.name, line_of(t, start));
            }
            if (subtype == NPCTYPE_SHOP || subtype == NPCTYPE_MARKETSHOP) && (value as f64) * 0.75 < itd.value_sell as f64 * 1.24 {
                // Exploit possible: you can buy and sell back with profit
                log::warn!(
                    "npc_parse_shop: Item {} [{nameid2}] discounted buying price ({value}->{}) is less than overcharged selling price ({}->{}) at file '{path}', line '{}'.",
                    itd.name, (value as f64 * 0.75) as i32, itd.value_sell, (itd.value_sell as f64 * 1.24) as i32, line_of(t, start)
                );
            }
            if subtype == NPCTYPE_MARKETSHOP && qty < -1 {
                log::warn!("npc_parse_shop: Item {} [{nameid2}] is stocked with invalid value {qty}, changed to unlimited (-1). File '{path}', line '{}'.", itd.name, line_of(t, start));
                qty = -1;
            }
            //for logs filters, atcommands and iteminfo script command: item_data::maxchance 0 becomes -1, "sold in NPC
            //Shop" (npc.cpp:4163-4165; the monsters' drop rates are loaded before the NPC files)
            if self.c.db().npc_shop_sold().nameid().find(nameid2).is_none() {
                self.c.db().npc_shop_sold().insert(NpcShopSold { nameid: nameid2 });
            }
            if subtype == NPCTYPE_MARKETSHOP && !items.is_empty() {
                // Duplicate entry? Replace the value (rAthena compares with `nameid`, the itemshop currency: 0 here)
                if let Some(e) = items.iter_mut().find(|e| e.nameid == itemshop_nameid) {
                    e.qty = qty;
                    e.value = value;
                    p = next_p;
                    continue;
                }
            }
            items.push(ShopItem { nameid: nameid2, value, qty: if subtype == NPCTYPE_MARKETSHOP { qty } else { 0 }, flag: 0 });
            p = next_p;
        }
        if items.is_empty() {
            log::warn!("npc_parse_shop: Ignoring empty shop in file '{path}', line '{}'.", line_of(t, start));
            return find_byte(t, start, b'\n');
        }
        let mut nd = Self::blank_id(nd_id, m, x, y, path);
        nd.shop = items;
        nd.itemshop_nameid = itemshop_nameid;
        nd.pointshop_str = point_str;
        nd.shop_discount = discount > 0;
        let (name, exname) = self.parse_name(w3, m, x, y, path);
        nd.name = name;
        nd.exname = exname;
        nd.class = if m == -1 { JT_FAKENPC } else { self.parse_view(w4, path) };
        nd.subtype = subtype;
        self.stats.shops += 1;
        // Insert market data to table (npc.cpp:4218-4223)
        if subtype == NPCTYPE_MARKETSHOP {
            for it in &nd.shop {
                crate::shop::npc_market_tosql(self.c, &nd.exname, it.nameid, it.value, it.qty, it.flag);
            }
        }
        self.insert(nd, dir as u8);
        find_byte(t, start, b'\n')
    }

    /// npc.cpp npc_parse_script: the header; the body is kept as a source span.
    #[allow(clippy::too_many_arguments)]
    fn parse_script(&mut self, w1: &[u8], w2: &[u8], w3: &[u8], w4: &[u8], start: usize, t: &[u8], path: &str, file_idx: u32) -> Option<usize> {
        let (m, x, y, dir) = if w1 == b"-" {
            (-1, 0, 0, 0)
        } else {
            let (n, mapname, x, y, d) = scan_placement(w1);
            if n != 4 {
                log::error!("npc_parse_script: Invalid placement format for a script in file '{path}', line '{}'. Skipping the rest of file...", line_of(t, start));
                return None;
            }
            (self.map_id(&mapname), x, y, d)
        };
        let end_of_line = find_byte(t, start, b'\n');
        let script_start = find(t, start, b",{");
        if find(w4, 0, b",{").is_none() || script_start.is_none() || (end_of_line.is_some() && script_start > end_of_line) {
            log::error!("npc_parse_script: Missing left curly ',{{' in file '{path}', line '{}'. Skipping the rest of the file.", line_of(t, start));
            return None;
        }
        let script_start = script_start.unwrap() + 1;
        let end = skip_script(t, script_start, path)?;
        let mut nd = self.blank(m, x, y, path);
        // `%*[^,],%6hd,%6hd`: the OnTouch area
        let mut s = Scan::new(w4);
        let _ = s.set(usize::MAX, b",");
        let (xs, ys) = if s.lit(b',') {
            match (s.int(6), s.lit(b',')) {
                (Some(a), true) => match s.int(6) {
                    Some(b) => (a as i16, b as i16),
                    None => (-1, -1),
                },
                _ => (-1, -1),
            }
        } else {
            (-1, -1)
        };
        nd.xs = xs;
        nd.ys = ys;
        let (name, exname) = self.parse_name(w3, m, x, y, path);
        nd.name = name;
        nd.exname = exname;
        nd.class = if m == -1 { JT_FAKENPC } else { self.parse_view(w4, path) };
        nd.subtype = NPCTYPE_SCRIPT;
        nd.script_file = file_idx;
        nd.script_start = script_start as u32;
        nd.script_end = end as u32;
        nd.script_line = line_of(t, script_start);
        nd.state = parse_state(w2, 7, path);
        self.stats.scripts += 1;
        let id = nd.id;
        self.insert(nd, dir as u8);
        // Loop through labels to export them as necessary (npc_event_export, npc_timerevent_export); timerid
        self.export_labels(id, "npc_parse_script");
        Some(end)
    }

    /// npc_parse_script / npc_parse_duplicate's label loop in Live mode (the other modes export after the load). The
    /// compile happens here (rAthena's parse_script runs before npc_create_npc; nothing between changes userfunc_db).
    fn export_labels(&mut self, id: u32, who: &str) {
        if self.mode != Mode::Live {
            return;
        }
        let Some(nd) = self.c.db().npc().id().find(id) else { return };
        let on_instance_map = nd.map >= 0 && crate::instance_map::map_instance_id(self.c, nd.map as u16) > 0;
        crate::script::npc_run::export_labels_rt(self.c, &nd, on_instance_map, who);
    }

    /// npc.cpp npc_parse_duplicate.
    #[allow(clippy::too_many_arguments)]
    fn parse_duplicate(&mut self, w1: &[u8], w2: &[u8], w3: &[u8], w4: &[u8], start: usize, t: &[u8], path: &str) -> Option<usize> {
        let end = find_byte(t, start, b'\n');
        let len = w2.len();
        if w2[len - 1] != b')' || len <= 11 || len - 11 >= 128 {
            log::error!("npc_parse_script: bad duplicate name in file '{path}', line '{}' : {}", line_of(t, start), text(w2));
            return end;
        }
        let srcname = text(&w2[10..len - 1]);
        let Some(src) = self.find_by_exname(&srcname) else {
            log::error!("npc_parse_script: original npc not found for duplicate in file '{path}', line '{}' : {srcname}", line_of(t, start));
            return end;
        };
        let src_type = src.subtype;
        let root_id = if src.src_id != 0 { src.src_id } else { src.id };
        let floating_ok = matches!(src_type, NPCTYPE_SHOP | NPCTYPE_CASHSHOP | NPCTYPE_ITEMSHOP | NPCTYPE_POINTSHOP | NPCTYPE_SCRIPT | NPCTYPE_MARKETSHOP);
        let (m, x, y, dir) = if floating_ok && w1 == b"-" {
            (-1, 0, 0, 0)
        } else {
            let (n, mapname, x, y, d) = scan_placement(w1);
            if n != 4 {
                log::error!("npc_parse_duplicate: Invalid placement format for duplicate in file '{path}', line '{}'. Skipping line...", line_of(t, start));
                return end;
            }
            (self.map_id(&mapname), x, y, d)
        };
        let (mut xs, mut ys) = (-1i16, -1i16);
        if src_type == NPCTYPE_WARP {
            let mut s = Scan::new(w4);
            match (s.int(6), s.lit(b','), s.int(6)) {
                (Some(a), true, Some(b)) => {
                    xs = a as i16;
                    ys = b as i16;
                }
                _ => {
                    log::error!("npc_parse_duplicate: Invalid span format for duplicate warp in file '{path}', line '{}'. Skipping line...", line_of(t, start));
                    return end;
                }
            }
        } else if src_type == NPCTYPE_SCRIPT {
            let mut s = Scan::new(w4);
            let _ = s.set(usize::MAX, b",");
            if s.lit(b',') {
                if let (Some(a), true, Some(b)) = (s.int(6), s.lit(b','), s.int(6)) {
                    xs = a as i16;
                    ys = b as i16;
                }
            }
        }
        let mut nd = self.blank(m, x, y, path);
        let (name, exname) = self.parse_name(w3, m, x, y, path);
        nd.name = name;
        nd.exname = exname;
        nd.class = if m == -1 { JT_FAKENPC } else { self.parse_view(w4, path) };
        nd.src_id = root_id;
        nd.subtype = src_type;
        match src_type {
            NPCTYPE_SCRIPT => {
                self.stats.scripts += 1;
                nd.xs = xs;
                nd.ys = ys;
                nd.script_file = src.script_file;
                nd.script_start = src.script_start;
                nd.script_end = src.script_end;
                nd.script_line = src.script_line;
            }
            NPCTYPE_SHOP | NPCTYPE_CASHSHOP | NPCTYPE_ITEMSHOP | NPCTYPE_POINTSHOP | NPCTYPE_MARKETSHOP => {
                self.stats.shops += 1;
                // safestrncpy(dst, src, strlen(src)): n counts the terminator, so the last character is lost
                let mut ps = src.pointshop_str.clone();
                ps.pop();
                nd.pointshop_str = ps;
                nd.itemshop_nameid = src.itemshop_nameid;
                nd.shop = src.shop.clone();
                nd.shop_discount = src.shop_discount;
                // the source's item list itself (shop_sharers)
                let mut f = self.c.db().npc_shop_family().root().find(root_id).unwrap_or(NpcShopFamily { root: root_id, members: vec![root_id] });
                f.members.push(nd.id);
                if self.c.db().npc_shop_family().root().find(root_id).is_some() {
                    self.c.db().npc_shop_family().root().update(f);
                } else {
                    self.c.db().npc_shop_family().insert(f);
                }
            }
            NPCTYPE_WARP => {
                self.stats.warps += 1;
                nd.class = JT_WARPNPC;
                nd.xs = xs;
                nd.ys = ys;
                nd.warp_map = src.warp_map;
                nd.warp_x = src.warp_x;
                nd.warp_y = src.warp_y;
                nd.trigger_on_hidden = src.trigger_on_hidden;
            }
            _ => {}
        }
        // copy the original npc state (npc_enable_target at run time: insert_rt)
        nd.state = src.state;
        let (id, is_script) = (nd.id, nd.subtype == NPCTYPE_SCRIPT);
        self.insert(nd, dir as u8);
        if is_script {
            self.export_labels(id, "npc_parse_duplicate");
        }
        end
    }

    /// npc.cpp npc_parse_duplicate called with its own w1..w4 (npc_duplicate_npc: the `duplicate` commands), over the
    /// one-line buffer rAthena passes ("- call from duplicate subsystem -\n", file "DUPLICATE"). The new NPC's id, or
    /// None when npc_parse_duplicate made nothing.
    pub(crate) fn duplicate_line(&mut self, w1: &[u8], w2: &[u8], w3: &[u8], w4: &[u8]) -> Option<u32> {
        const STAT_BUF: &[u8] = b"- call from duplicate subsystem -\n";
        let before = self.made.len();
        self.parse_duplicate(w1, w2, w3, w4, 0, STAT_BUF, "DUPLICATE");
        self.made.get(before).copied()
    }

    /// npc.cpp npc_parse_function.
    fn parse_function(&mut self, w3: &[u8], w4: &[u8], start: usize, t: &[u8], path: &str, file_idx: u32) -> Option<usize> {
        let end_of_line = find_byte(t, start, b'\n');
        let script_start = find(t, start, b"\t{");
        if w4.first() != Some(&b'{') || script_start.is_none() || (end_of_line.is_some() && script_start > end_of_line) {
            log::error!("npc_parse_function: Missing left curly '%TAB%{{' in file '{path}', line '{}'. Skipping the rest of the file.", line_of(t, start));
            return None;
        }
        let script_start = script_start.unwrap() + 1;
        let end = skip_script(t, script_start, path)?;
        let name = text(w3);
        // parse_script: a function that does not compile is not registered (parse error, continue)
        if !crate::script::code::define_function(self.c, &name, file_idx, script_start as u32, line_of(t, start)) {
            return Some(end);
        }
        let row = NpcFunction { name: name.clone(), script_file: file_idx, script_start: script_start as u32, script_end: end as u32, script_line: line_of(t, start) };
        if self.c.db().npc_function().name().find(&name).is_some() {
            log::info!("npc_parse_function: Overwriting user function [{name}] ({path})");
            // script_free_code(oldscript): the old code's `.` variables go with it
            crate::npc_load::free_code_vars(self.c, &crate::script::vm::CodeKey::Func(name.clone()));
            self.c.db().npc_function().name().update(row);
        } else {
            self.c.db().npc_function().insert(row);
        }
        self.stats.functions += 1;
        Some(end)
    }

    /// npc.cpp npc_parse_mob: records the spawn line (monsters appear with phase 3).
    #[allow(clippy::too_many_arguments)]
    fn parse_mob(&mut self, w1: &[u8], w2: &str, w3: &[u8], w4: &[u8], start: usize, t: &[u8], path: &str) -> Option<usize> {
        let next = find_byte(t, start, b'\n');
        let boss = w2 == "boss_monster";
        // w1=<map name>{,<x>,<y>{,<xs>,<ys>}}
        let mut s = Scan::new(w1);
        let Some(mapname) = s.set(15, b",") else {
            log::error!("npc_parse_mob: Invalid mob definition in file '{path}', line '{}'.", line_of(t, start));
            return next;
        };
        let mut nums = [0i64; 4];
        for n in nums.iter_mut() {
            if !s.lit(b',') {
                break;
            }
            match s.int(6) {
                Some(v) => *n = v,
                None => break,
            }
        }
        let (x, y, mut xs, mut ys) = (nums[0] as i16, nums[1] as i16, nums[2] as i16, nums[3] as i16);
        // w3=<mob name>{,<mob level>}
        let mut s3 = Scan::new(w3);
        let Some(mobname) = s3.set(23, b",") else {
            log::error!("npc_parse_mob: Invalid mob definition in file '{path}', line '{}'.", line_of(t, start));
            return next;
        };
        let mob_lv: i64 = if s3.lit(b',') { s3.int(11).unwrap_or(-1) } else { -1 };
        // w4=<mob id>,<amount>{,<delay1>{,<delay2>{,<event>{,<mob size>{,<mob ai>}}}}}
        let mut s4 = Scan::new(w4);
        let sprite = s4.set(23, b",");
        let num = if s4.lit(b',') { s4.int(11) } else { None };
        let (Some(sprite), Some(num)) = (sprite, num) else {
            log::error!("npc_parse_mob: Invalid mob definition in file '{path}', line '{}'.", line_of(t, start));
            return next;
        };
        let mut w4count = 2;
        let mut delay: i64 = 5000;
        let mut delay2: i64 = 0;
        let mut eventname = String::new();
        let mut size: i64 = -1;
        let mut ai: i64 = 0;
        if s4.lit(b',') {
            if let Some(v) = s4.int(11) {
                delay = v;
                w4count += 1;
                if s4.lit(b',') {
                    if let Some(v) = s4.int(11) {
                        delay2 = v;
                        w4count += 1;
                        if s4.lit(b',') {
                            if let Some(ev) = s4.set(77, b",") {
                                eventname = ev;
                                w4count += 1;
                                if s4.lit(b',') {
                                    if let Some(v) = s4.int(11) {
                                        size = v;
                                        w4count += 1;
                                        if s4.lit(b',') {
                                            if let Some(v) = s4.int(11) {
                                                ai = v;
                                                w4count += 1;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        let _ = w4count;
        if path == "npc/re/mobs/academy.txt" && mapname.eq_ignore_ascii_case("prt_fild08") {
            // prt_fild08 already has its full regional spawn set in npc/re/mobs/fields/prontera.txt;
            // academy.txt is loaded at boot for prt_fild08a-d, treasure_n1-2 and new_1-3.
            return next;
        }
        let m = self.map_id(&mapname);
        if m < 0 {
            return next;
        }
        let md = data::maps().get(m as u16).unwrap();
        if x < 0 || x >= md.xs || y < 0 || y >= md.ys {
            log::error!("npc_parse_mob: Spawn coordinates out of range: {} ({x},{y}) (file '{path}', line '{}').", md.name, line_of(t, start));
            return next;
        }
        let mob_id = match parse_c_long(&sprite) {
            Some(v) => {
                if !data::mobdb_checkid(v as i32) {
                    log::error!("npc_parse_mob: Unknown mob ID {v} (file '{path}', line '{}').", line_of(t, start));
                    return next;
                }
                v as i32
            }
            None => match data::mobdb_search_aegisname(&sprite) {
                Some(id) => id as i32,
                None => {
                    log::error!("npc_parse_mob: Unknown mob name {sprite} (file '{path}', line '{}').", line_of(t, start));
                    return next;
                }
            },
        };
        if !(1..=1000).contains(&num) {
            log::error!("npc_parse_mob: Invalid number of monsters {num} (file '{path}', line '{}').", line_of(t, start));
            return next;
        }
        // mob_respawn_time: 1000 (monster.conf) — shorter explicit delays fall back to 5 s
        let delay1 = if w4count > 2 && delay != 5000 && delay < crate::conf::mob_respawn_time() { 5000 } else { delay };
        if (mob_lv == 0 || mob_lv > 275) && mob_lv != -1 {
            log::error!("npc_parse_mob: Invalid level {mob_lv} for mob ID {mob_id} (file '{path}', line '{}').", line_of(t, start));
            return next;
        }
        let mut sp = SpawnData {
            id: 0,
            mob_id,
            map: m as u16,
            x,
            y,
            xs: 0,
            ys: 0,
            num: num as u16,
            active: 0,
            delay1: delay1 as u32,
            delay2: delay2 as u32,
            level: if mob_lv > 0 && mob_lv <= 275 { mob_lv as u32 } else { 0 },
            size: if size > 0 && size <= 2 { size as u8 } else { 0 },
            ai: if ai > 0 && ai <= crate::mob::AI_MAX as i64 { ai as u8 } else { 0 },
            boss,
            dynamic: crate::conf::dynamic_mobs(),
            name: mobname,
            eventname,
            path: path.to_string(),
        };
        if xs < 0 {
            xs = 0;
        } else if xs == 0 && x > 0 {
            xs = 1;
        }
        if ys < 0 {
            ys = 0;
        } else if ys == 0 && y > 0 {
            ys = 1;
        }
        sp.xs = xs;
        sp.ys = ys;
        // mob_count_rate: 100; force_random_spawn: no
        if (sp.x == 0 && sp.y == 0) || (sp.xs == 1 && sp.ys == 1 && !map::getcellp(md, sp.x, sp.y, CellChk::ChkReach)) {
            sp.x = -1;
            sp.y = -1;
            sp.xs = 0;
            sp.ys = 0;
        }
        // mob_spawn_variance: 1 (only bosses vary)
        if (crate::conf::mob_spawn_variance() & 1 == 0 && sp.boss) || (crate::conf::mob_spawn_variance() & 2 == 0 && !sp.boss) {
            sp.delay2 = 0;
        }
        if sp.delay1 > 0xfffffff || sp.delay2 > 0xfffffff {
            log::error!("npc_parse_mob: Invalid spawn delays {} {} (file '{path}', line '{}').", sp.delay1, sp.delay2, line_of(t, start));
            return next;
        }
        // Use db names instead of the spawn file ones (override_mob_names: 0 keeps the file's), then verify the
        // dataset (mob_parse_dataset: event name quotes, "--en--" / "--ja--")
        if !crate::mob::spawn::mob_parse_dataset(self.c, sp.mob_id, sp.num, &mut sp.name, &mut sp.eventname) {
            log::error!("npc_parse_mob: Invalid dataset for monster ID {mob_id} (file '{path}', line '{}').", line_of(t, start));
            return next;
        }
        let row = self.c.db().spawn_data().insert(sp);
        // spawn / cache the new mobs (map_addmobtolist with dynamic_mobs, npc_parse_mob2): the monster system
        crate::mob::spawn::npc_parse_mob_spawn(self.c, row.id);
        self.stats.mobs += 1;
        next
    }

    /// npc.cpp npc_parse_mapflag.
    fn parse_mapflag(&mut self, w1: &[u8], w3: &[u8], w4: &[u8], start: usize, t: &[u8], path: &str) -> Option<usize> {
        let next = find_byte(t, start, b'\n');
        let mut s = Scan::new(w1);
        let Some(mapname) = s.set(15, b",") else {
            log::error!("npc_parse_mapflag: Invalid mapflag definition in file '{path}', line '{}'.", line_of(t, start));
            return next;
        };
        let m = self.map_id(&mapname);
        if m < 0 {
            log::warn!("npc_parse_mapflag: Unknown map '{mapname}' in file '{path}', line '{}'.", line_of(t, start));
            return next;
        }
        let m = m as u16;
        let state = !w4.eq_ignore_ascii_case(b"off");
        let flag_name = text(w3);
        let Some(flag) = data::constant(&format!("mf_{flag_name}")) else {
            log::error!("npc_parse_mapflag: unrecognized mapflag '{flag_name}' (file '{path}', line '{}').", line_of(t, start));
            return next;
        };
        let flag = flag as i16;
        let int_arg = |d: i32| -> i32 { Scan::new(w4).int(11).map(|v| v as i32).unwrap_or(d) };
        match flag {
            MF_NOSAVE => {
                let sp = if state && w4.eq_ignore_ascii_case(b"SavePoint") {
                    Some((-1, -1, -1))
                } else if state {
                    let mut s = Scan::new(w4);
                    match (s.set(15, b","), s.lit(b','), s.int(6), s.lit(b','), s.int(6)) {
                        (Some(sm), true, Some(sx), true, Some(sy)) => match data::maps().id_of(&sm) {
                            Some(id) => Some((id as i32, sx as i16, sy as i16)),
                            None => {
                                log::warn!("npc_parse_mapflag: Specified save point map '{sm}' for mapflag 'nosave' not found (file '{path}'), using 'SavePoint'.");
                                Some((-1, -1, -1))
                            }
                        },
                        _ => None,
                    }
                } else {
                    None
                };
                if let Some((sm, sx, sy)) = sp {
                    let row = MapSavePoint { map: m, save_map: sm, x: sx, y: sy };
                    if self.c.db().map_save_point().map().find(m).is_some() {
                        self.c.db().map_save_point().map().update(row);
                    } else {
                        self.c.db().map_save_point().insert(row);
                    }
                }
                setmapflag(self.c, m, MF_NOSAVE, state, 0);
            }
            MF_PVP_NIGHTMAREDROP => {
                // "%15[^,],%15[^,],%11d": an existing item id (or "random") sets the flag — on, even with "off" —
                // and a drop list entry (kept with the PvP drop rules, phase 7); "off" without arguments clears it;
                // anything else sets nothing (npc.cpp:5467-5491)
                let mut s = Scan::new(w4);
                let args = match (s.set(15, b","), s.lit(b','), s.set(15, b","), s.lit(b','), s.int(11)) {
                    (Some(a1), true, Some(_a2), true, Some(_per)) => Some(a1),
                    _ => None,
                };
                match args {
                    Some(a1) => {
                        let drop_id: i64 = if a1.eq_ignore_ascii_case("random") { -1 } else { crate::cstr::atoi(&a1) as i64 };
                        if drop_id != -1 && crate::db::items::itemdb_exists(drop_id as u32).is_none() {
                            log::warn!("npc_parse_mapflag: Invalid item ID '{drop_id}' supplied for mapflag 'pvp_nightmaredrop' (file '{path}'), removing.");
                        } else if drop_id != 0 {
                            setmapflag(self.c, m, flag, true, 0);
                        }
                    }
                    None if !state => setmapflag(self.c, m, flag, false, 0),
                    None => {}
                }
            }
            MF_BATTLEGROUND => setmapflag(self.c, m, flag, state, if state { int_arg(1) } else { 0 }),
            MF_NOCOMMAND => setmapflag(self.c, m, flag, state, if state { int_arg(100) } else { 0 }),
            MF_RESTRICTED => {
                if state {
                    match Scan::new(w4).int(11) {
                        Some(v) => setmapflag(self.c, m, flag, true, v as i32),
                        None => log::warn!("npc_parse_mapflag: Zone value not set for the restricted mapflag! Skipped flag from {mapname} (file '{path}')."),
                    }
                } else {
                    // map_setmapflag(m, MF_RESTRICTED, false): no zone argument, so every zone goes (map.cpp:4871-4874)
                    set_flag_raw(self.c, m, MF_RESTRICTED, 0);
                }
            }
            MF_JEXP | MF_BEXP | MF_SPECIALPOPUP => setmapflag(self.c, m, flag, state, int_arg(0)),
            MF_SKILL_DAMAGE => {
                // npc.cpp:5551-5594: "%30[^,],%23[^,],%11d,%11d,%11d,%11d" with at least three fields; the caster is
                // digits or a constant (0 = BL_ALL), the rates are capped; "all" sets mapdata->damage_adjust
                // (map_setmapflag_sub), a skill name only the flag (flag_val 1) and its skill_damage entry
                if !state {
                    // map_setmapflag_sub(m, MF_SKILL_DAMAGE, false): damage_adjust = {}, skill_damage.clear()
                    crate::map_skill_damage::clear(self.c, m);
                    set_flag_raw(self.c, m, flag, 0);
                } else if let Some((skill, caster, rate)) = crate::map_skill_damage::parse_w4(w4) {
                    let caster_v = if caster.as_bytes().first().map(|b| b.is_ascii_digit()).unwrap_or(false) {
                        Some(crate::cstr::atoi(&caster) as u16)
                    } else {
                        data::constant(&caster).map(|v| v as u16)
                    };
                    match caster_v {
                        None => log::error!("npc_parse_mapflag: Unknown constant '{caster}'. Skipping (file '{path}')."),
                        Some(caster) => {
                            let mut args = crate::map_skill_damage::SkillDamage { caster, rate };
                            if args.caster == 0 {
                                args.caster = crate::map::BL_ALL;
                            }
                            for r in args.rate.iter_mut() {
                                *r = (*r).clamp(-100, 100000);
                            }
                            let skill = skill.trim();
                            let skill_id = crate::db::skills::skill_name2id(skill);
                            if skill == "all" {
                                // Adjust damage for all skills
                                if crate::map_skill_damage::set_damage_adjust(self.c, m, &args) {
                                    set_flag_raw(self.c, m, flag, 1);
                                }
                            } else if skill_id == 0 {
                                log::warn!("npc_parse_mapflag: Invalid skill name '{skill}' for Skill Damage mapflag. Skipping (file '{path}').");
                            } else {
                                // Adjusted damage for specified skill
                                set_flag_raw(self.c, m, flag, 1);
                                crate::map_skill_damage::map_skill_damage_add(self.c, m, skill_id, &args);
                            }
                        }
                    }
                }
            }
            MF_SKILL_DURATION => {
                // "%30[^,],%5hu" with a known skill (npc.cpp:5597-5616); map.cpp map_setmapflag_sub keeps the
                // durations in mapdata->skill_duration (skillunit's map_skill_duration table; skill_get_time3 reads it)
                if !state {
                    crate::skillunit::data::map_skill_duration_clear(self.c, m);
                    set_flag_raw(self.c, m, flag, 0);
                } else {
                    let mut s = Scan::new(w4);
                    let skill = s.set(30, b",");
                    let per = skill.as_ref().and_then(|_| if s.lit(b',') { s.int(5) } else { None });
                    if let (Some(skill), Some(per)) = (skill, per) {
                        let skill_id = crate::db::skills::skill_name2id(&skill);
                        if skill_id == 0 {
                            log::error!("npc_parse_mapflag: skill_duration: Invalid skill name '{skill}' for Skill Duration mapflag. Skipping (file '{path}')");
                        } else {
                            crate::skillunit::data::map_skill_duration_add(self.c, m, skill_id, per as u16);
                            set_flag_raw(self.c, m, flag, 1);
                        }
                    }
                }
            }
            MF_INVINCIBLE_TIME => {
                if state {
                    match Scan::new(w4).int(11) {
                        Some(v) => setmapflag(self.c, m, flag, true, v as i32),
                        None => log::error!("npc_parse_mapflag: invincible_time: Invalid time for Invincible Time mapflag. Skipping (file '{path}')"),
                    }
                } else {
                    setmapflag(self.c, m, flag, false, 0);
                }
            }
            // map_setmapflag at run time: PvP / GvG tell the players on the map (map property, PvP ranking, attacks
            // stopped: battle/pvp.rs); at start-up nobody is there to tell
            _ if self.mode != Mode::Boot => crate::battle::pvp::map_setmapflag(self.c, m, flag, state, 0),
            _ => setmapflag(self.c, m, flag, state, 0),
        }
        self.stats.mapflags += 1;
        next
    }
}

/// strtol(s, &end, 0) consuming the whole string: decimal, 0x hex, 0 octal.
fn parse_c_long(s: &str) -> Option<i64> {
    let t = s.trim_start();
    if t.is_empty() {
        return None;
    }
    let (neg, body) = match t.as_bytes()[0] {
        b'-' => (true, &t[1..]),
        b'+' => (false, &t[1..]),
        _ => (false, t),
    };
    let v = if let Some(h) = body.strip_prefix("0x").or_else(|| body.strip_prefix("0X")) {
        i64::from_str_radix(h, 16).ok()?
    } else if body.len() > 1 && body.starts_with('0') {
        i64::from_str_radix(&body[1..], 8).ok()?
    } else {
        body.parse::<i64>().ok()?
    };
    Some(if neg { -v } else { v })
}

/// `script(<STATE>)` / `warp(<STATE>)`: CLOAKED / HIDDEN / DISABLED.
fn parse_state(w2: &[u8], shift: usize, path: &str) -> u8 {
    let len = w2.len();
    if len <= shift - 1 || w2.len() == shift - 1 {
        return NPCVIEW_ENABLE;
    }
    // strcasecmp("script", w2) < 0 means w2 has more after the keyword
    if len < shift {
        return NPCVIEW_ENABLE;
    }
    if w2[shift - 1] != b'(' || w2[len - 1] != b')' || len <= shift {
        log::warn!("npc: Invalid npc state in file '{path}', defaulting to visible. w2={}", text(w2));
        return NPCVIEW_ENABLE;
    }
    let st = text(&w2[shift..len - 1]).to_ascii_uppercase();
    match st.as_str() {
        "CLOAKED" => NPCVIEW_CLOAKON,
        "HIDDEN" => NPCVIEW_HIDEON,
        "DISABLED" => NPCVIEW_DISABLE,
        _ => {
            log::warn!("npc: Invalid npc state in file '{path}', defaulting to visible. w2={}", text(w2));
            NPCVIEW_ENABLE
        }
    }
}

/// status.cpp status_set_viewdata for an NPC class (npc view db, monster sprites, player sprites).
pub fn npc_viewdata(class: i32) -> ViewData {
    let mut vd = ViewData::default();
    let npcdb = |id: i32| (id > 44 && id < 126) || id == JT_HIDDEN_WARP_NPC || (id > 400 && id < 1000) || id == JT_INVISIBLE || (id > 10000 && id < 20000);
    if class == JT_FAKENPC {
        vd.class = JT_INVISIBLE;
    } else if data::mobdb_checkid(class) || npcdb(class) {
        vd.class = class;
    } else if crate::db::misc::homunculus::homdb_checkid(class) {
        // homdb_checkid(class_): hom_get_viewdata (hom_viewdb: look[LOOK_BASE] = the class; homunculus.rs)
        vd.class = class;
    } else if clif::pcdb_checkid(class) {
        vd.class = class;
        vd.body2 = class;
        vd.hair = 0i32.clamp(crate::conf::min_hair_style(), crate::conf::max_hair_style());
    } else {
        log::error!("status_set_viewdata (NPC): Invalid view data {class}");
        vd.class = JT_INVISIBLE;
    }
    vd
}

// ---------------------------------------------------------------------------------------------- OnTouch areas

pub(crate) fn touch_npcs(c: &Ctx, m: u16) -> Vec<Npc> {
    let mut v: Vec<Npc> = c.db().npc().by_map().filter(m as i32).filter(|n| n.touch_order > 0).collect();
    v.sort_by_key(|n| n.touch_order);
    v
}

/// CELL_CHKNPC: the cell lies in an OnTouch area (npc_setcells marks the passable cells of each area).
pub fn has_touch_area_at(c: &Ctx, m: u16, x: i16, y: i16) -> bool {
    let Some(md) = data::maps().get(m) else { return false };
    if map::getcellp(md, x, y, CellChk::ChkNoPass) {
        return false;
    }
    c.db().npc().by_map().filter(m as i32).any(|n| n.touch_order > 0 && x >= n.x - n.xs && x <= n.x + n.xs && y >= n.y - n.ys && y <= n.y + n.ys)
}

/// npc.cpp npc_check_areanpc: an NPC (flag &1 warps, &2 OnTouch scripts) whose area overlaps the range around (x, y).
pub fn check_areanpc(c: &Ctx, flag: i32, m: u16, x: i16, y: i16, range: i16) -> bool {
    if range < 0 {
        return false;
    }
    let Some(md) = data::maps().get(m) else { return false };
    let x0 = (x - range).max(0);
    let y0 = (y - range).max(0);
    let x1 = (x + range).min(md.xs - 1);
    let y1 = (y + range).min(md.ys - 1);
    let mut any_cell = false;
    'outer: for yy in y0..=y1 {
        for xx in x0..=x1 {
            if has_touch_area_at(c, m, xx, yy) {
                any_cell = true;
                break 'outer;
            }
        }
    }
    if !any_cell {
        return false;
    }
    touch_npcs(c, m).iter().any(|n| {
        if n.is_invisible || n.owner_char != 0 {
            return false;
        }
        let ok = match n.subtype {
            NPCTYPE_WARP => flag & 1 != 0,
            NPCTYPE_SCRIPT => flag & 2 != 0,
            _ => false,
        };
        ok && x1 >= n.x - n.xs && x0 <= n.x + n.xs && y1 >= n.y - n.ys && y0 <= n.y + n.ys
    })
}

/// npc.cpp npc_touch_area_allnpc: the player stepped on a touch cell.
pub fn touch_area_allnpc(c: &Ctx, aid: u32, m: u16, x: i16, y: i16) {
    // unit.cpp's walk / move callers run npc_touchnext_areanpc(sd, false) first (see pc::clear_areanpc)
    crate::script::npc_run::npc_touchnext_areanpc(c, aid, false);
    let Some(mut p) = crate::pc::get(c, aid) else { return };
    // Remove NPCs that are no longer within the OnTouch area
    if prune_areanpc(c, &mut p.areanpc, m, x, y) {
        crate::pc::put(c, &p);
    }
    if p.block_action & crate::pc::PCBLOCK_NPCCLICK != 0 {
        return;
    }
    let mut found = false;
    for n in touch_npcs(c, m) {
        match touch_areanpc(c, aid, m, x, y, &n) {
            0 => {}
            1 => found = true,
            _ => return,
        }
    }
    if !found {
        log::error!("npc_touch_area_allnpc : stray NPC cell/NPC not found in the block on coordinates '{}',{x},{y}", data::maps().get(m).map(|d| d.name.as_str()).unwrap_or("?"));
    }
}

/// npc.cpp npc_unload(nd, true): takes the NPC off its map (npc_remove_map) and out of the world — its events,
/// timers and sleeping scripts with it (the script part is in script::npc_run).
pub fn unload(c: &Ctx, id: u32) {
    unload_ex(c, id, true);
}

/// npc.cpp npc_unload(nd, single). `single` false is npc_reload's call, made after npcname_db and ev_db were cleared:
/// the NPC's name and events are not looked for, and its questinfo goes (nd->qi_data.clear()) without
/// map_remove_questinfo (map_flags_init frees the maps' lists afterwards).
pub fn unload_ex(c: &Ctx, id: u32, single: bool) {
    let Some(nd) = c.db().npc().id().find(id) else { return };
    // status_change_clear(nd, 1) (only an NPC with status changes has anything to clear)
    if !crate::status::sc::load(c, id).empty() {
        crate::status::sc::status_change_clear(c, id, 1);
    }
    remove_map(c, &nd);
    // remove npc chatroom object and kick users (npc.cpp:3486-3487)
    if crate::chat::npc_chat_id(c, id) != 0 {
        crate::chat::chat_deletenpcchat(c, id);
    }
    crate::quest::map_remove_questinfo(c, nd.id, if single { nd.map } else { -1 });
    crate::script::npc_run::on_npc_unload_ex(c, &nd, single);
    crate::instance_npc::on_npc_unload(c, id); // nd->instance_id goes with the npc_data
    crate::pc_misc::npc_unload(c, id); // the script2 fields and npc_chat_finalize
    // if( nd->u.scr.guild_id ) guild_flag_remove(nd); (woe.rs)
    crate::woe::npc_unload(c, &nd);
    // nd->dynamicnpc: the removal timer, the owner's npc_id_dynamic (npc_load.rs)
    crate::npc_load::on_npc_unload(c, &nd);
    c.db().unit().id().delete(id);
    c.db().npc().id().delete(id);
}

/// npc.cpp npc_remove_map: the NPC vanishes (CLR_RESPAWN) and leaves mapdata->npc. Removing from the trigger-area
/// section moves its last script (or, for a warp, its last warp and then its last NPC) into the hole.
pub(crate) fn remove_map(c: &Ctx, nd: &Npc) {
    let Some(u) = crate::unit::get(c, nd.id) else { return };
    if !u.on_map || nd.map < 0 {
        return; // not assigned to a map
    }
    // npc.cpp:3414-3415: a script NPC's unit groups (the warpportal command's portals) go with it (skillunit)
    if nd.subtype == NPCTYPE_SCRIPT {
        crate::skillunit::skill_clear_unitgroup(c, nd.id);
    }
    let Some(u) = crate::unit::get(c, nd.id) else { return };
    clif::clearunit_area(c, &u, crate::unit::CLR_RESPAWN);
    let mut gone = u;
    gone.on_map = false;
    crate::unit::save(c, &gone);
    if nd.touch_order == 0 {
        return;
    }
    let mut area = touch_npcs(c, nd.map as u16);
    let num_warp = area.iter().filter(|n| n.subtype == NPCTYPE_WARP).count();
    let Some(i) = area.iter().position(|n| n.id == nd.id) else { return };
    let num_area = area.len();
    if i >= num_warp {
        // npc_num_area--; npc[i] = npc[npc_num_area];
        area[i] = area[num_area - 1].clone();
    } else {
        // npc_num_warp--; npc_num_area--; npc[i] = npc[npc_num_warp]; npc[npc_num_warp] = npc[npc_num_area];
        area[i] = area[num_warp - 1].clone();
        area[num_warp - 1] = area[num_area - 1].clone();
    }
    area.truncate(num_area - 1);
    for (k, n) in area.into_iter().enumerate() {
        if n.touch_order != k as u32 + 1 {
            c.db().npc().id().update(Npc { touch_order: k as u32 + 1, ..n });
        }
    }
    if let Some(cur) = c.db().npc().id().find(nd.id) {
        c.db().npc().id().update(Npc { touch_order: 0, ..cur });
    }
}

/// "Remove NPCs that are no longer within the OnTouch area" (npc_touch_area_allnpc, unit_walktoxy_ontouch): the loop
/// erases with util::erase_at and still steps i forward, so the entry after an erased one is not checked. Returns
/// whether anything was erased.
pub fn prune_areanpc(c: &Ctx, areanpc: &mut Vec<u32>, m: u16, x: i16, y: i16) -> bool {
    let mut changed = false;
    let mut i = 0;
    while i < areanpc.len() {
        let keep = match c.db().npc().id().find(areanpc[i]) {
            Some(n) => n.subtype == NPCTYPE_SCRIPT && n.map == m as i32 && x >= n.x - n.xs && x <= n.x + n.xs && y >= n.y - n.ys && y <= n.y + n.ys,
            None => false,
        };
        if !keep {
            areanpc.remove(i);
            changed = true;
        }
        i += 1;
    }
    changed
}

/// unit.cpp unit_walktoxy_ontouch: a player standing in an NPC's OnTouch area when the NPC appears.
pub fn walktoxy_ontouch(c: &Ctx, aid: u32, n: &Npc) {
    let Some(mut p) = crate::pc::get(c, aid) else { return };
    if p.block_action & crate::pc::PCBLOCK_NPCCLICK != 0 {
        return;
    }
    let Some(u) = crate::unit::get(c, aid) else { return };
    if prune_areanpc(c, &mut p.areanpc, u.map, u.x, u.y) {
        crate::pc::put(c, &p);
    }
    crate::script::npc_run::npc_touchnext_areanpc(c, aid, false);
    let Some(u) = crate::unit::get(c, aid) else { return };
    if !map::getcell(c, u.map, u.x, u.y, map::CellChk::ChkNpc) {
        return;
    }
    touch_areanpc(c, aid, u.map, u.x, u.y, n);
}

/// npc.cpp npc_touch_areanpc: 0 not in the area, 1 handled / blocked, 2 warped.
pub fn touch_areanpc(c: &Ctx, aid: u32, _m: u16, x: i16, y: i16, n: &Npc) -> i32 {
    if n.is_invisible {
        return 1;
    }
    // npc_is_cloaked: the NPC's own OPTION_CLOAK, flipped for this player by cloakonnpcself / cloakoffnpcself
    let npc_cloaked = crate::unit::get(c, n.id).map(|u| u.sc.option & OPTION_CLOAK != 0).unwrap_or(false);
    if npc_cloaked != crate::pc::npc_cloaked_for(c, aid, n.id) {
        return 1;
    }
    let Some(sd) = crate::unit::get(c, aid) else { return 0 };
    if n.owner_char != 0 && n.owner_char != sd.char_id {
        return 1;
    }
    if n.xs < 0 || n.ys < 0 {
        return 0;
    }
    if x < n.x - n.xs || x > n.x + n.xs || y < n.y - n.ys || y > n.y + n.ys {
        return 0;
    }
    match n.subtype {
        NPCTYPE_WARP => {
            // pc_ishiding(sd) || sd->sc.getSCE(SC_CAMOUFLAGE) (npc.cpp:1930)
            let hiding = sd.sc.option & (OPTION_HIDE | OPTION_CLOAK | crate::unit::OPTION_CHASEWALK) != 0
                || crate::status::sc::has_sce(c, aid, crate::status::consts::SC_CAMOUFLAGE);
            let dead = crate::pc::get(c, aid).map(|p| p.dead_sit == 1).unwrap_or(false);
            if (!n.trigger_on_hidden && hiding) || dead {
                return 1; // hidden or dead chars cannot use warps
            }
            if n.warp_map < 0 {
                return 1;
            }
            let Some(mut p) = crate::pc::get(c, aid) else { return 1 };
            let class = crate::pc::character_of(c, aid).map(|ch| ch.class as i32).unwrap_or(0);
            if !crate::pc::job_can_entermap(c, class, n.warp_map as u16, crate::rules::group_level(p.group_id)) {
                return 1;
            }
            if p.count_rewarp > 10 {
                log::warn!("Prevented infinite warp loop for player ({aid}:{}). Please fix NPC: '{}', path: '{}'", p.char_id, n.exname, n.path);
                p.count_rewarp = 0;
                crate::pc::put(c, &p);
                return 1;
            }
            crate::pc::setpos(c, aid, n.warp_map as u16, n.warp_x, n.warp_y, crate::unit::CLR_OUTSIGHT);
            2
        }
        NPCTYPE_SCRIPT => {
            // OnTouch_ / OnTouch events, or a click when the NPC has neither
            crate::script::ontouch(c, aid, n);
            1
        }
        _ => 1,
    }
}

/// clif_getareachar_unit's NPC extras: the waiting room (chat.rs); size effect, status icons, progress bar: with their
/// systems.
pub fn getareachar_npc(c: &Ctx, _sd: &Unit, nd: &Unit) {
    crate::chat::getareachar_npc(c, nd);
}

/// clif.cpp clif_weather_check.
pub fn weather_check(c: &Ctx, sd: &Unit) {
    let m = sd.map;
    let eff = |e: u32| {
        let mut w = Vec::with_capacity(10);
        w.extend_from_slice(&0x01f3u16.to_le_bytes());
        w.extend_from_slice(&sd.id.to_le_bytes());
        w.extend_from_slice(&e.to_le_bytes());
        clif::send(c, &w, sd, clif::Target::SelfOnly);
    };
    if mapflag(c, m, MF_SNOW) {
        eff(162); // EF_SNOW
    }
    if mapflag(c, m, MF_CLOUDS) {
        eff(233); // EF_CLOUD3
    }
    if mapflag(c, m, MF_CLOUDS2) {
        eff(516); // EF_CLOUD5
    }
    if mapflag(c, m, MF_FOG) {
        eff(515); // EF_CLOUD4
    }
    if mapflag(c, m, MF_FIREWORKS) {
        eff(297); // EF_POKJUK
        eff(299); // EF_THROWITEM2
        eff(301); // EF_POKJUK_SOUND
    }
    if mapflag(c, m, MF_SAKURA) {
        eff(163); // EF_SAKURA
    }
    if mapflag(c, m, MF_LEAVES) {
        eff(333); // EF_MAPLE
    }
}
