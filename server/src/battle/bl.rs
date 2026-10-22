//! Accessors battle.cpp reaches through block_list casts: the player's map_session_data (status rows, item row,
//! character row), status_get_status_data / status_get_sc and the status.hpp getters. Each read is fresh, as rAthena's
//! pointers always see the current data; callers re-read after anything that may change it.

use crate::character::Character;
use crate::inventory::PcItems;
use crate::map::{BL_MOB, BL_NPC, BL_PC};
use crate::status::data::{PcStatus, StatusData};
use crate::status::sc::{self, Sc};
use crate::unit::Unit;
use crate::world::Ctx;

/// A player's map_session_data as battle reads it.
#[derive(Clone)]
pub struct Sd {
    pub aid: u32,
    /// the status system's row (battle_status, bonuses, weapons, special_state, class_ …)
    pub st: PcStatus,
    /// the items system's row (weapontype1 / 2, equip_index)
    pub it: PcItems,
    /// sd->status (mmo_charstatus)
    pub ch: Character,
}

/// BL_CAST(BL_PC, bl).
pub fn sd(c: &Ctx, u: &Unit) -> Option<Sd> {
    if u.kind != BL_PC {
        return None;
    }
    let st = crate::status::data::sd_get(c, u.id)?;
    let it = crate::inventory::st(c, u.id)?;
    let ch = crate::pc::character_of(c, u.id)?;
    Some(Sd { aid: u.id, st, it, ch })
}

/// status_get_status_data.
pub fn status(c: &Ctx, u: &Unit) -> StatusData {
    crate::status::status_get_status_data(c, u)
}

/// status_get_sc: None for the unit kinds that have no status_change.
pub fn sc(c: &Ctx, u: &Unit) -> Option<Sc> {
    super::mobif::has_sc(u.kind).then(|| sc::load(c, u.id))
}

/// The `sc` of battle functions that skip their checks when nothing is active (`if (sc && sc->empty()) sc = nullptr`).
pub fn sc_nonempty(c: &Ctx, u: &Unit) -> Option<Sc> {
    sc(c, u).filter(|s| !s.empty())
}

/// pc_checkskill (0 without a player).
pub fn checkskill(c: &Ctx, sd: Option<&Sd>, skill_id: u16) -> i32 {
    sd.map(|s| crate::skill::checkskill(c, s.aid, skill_id) as i32).unwrap_or(0)
}

/// status.cpp status_get_class (monsters: their view class; NPCs: their class).
pub fn class_of(c: &Ctx, u: &Unit) -> i32 {
    match u.kind {
        BL_PC => crate::pc::character_of(c, u.id).map(|ch| ch.class as i32).unwrap_or(0),
        BL_MOB | BL_NPC => u.vd.class,
        // BL_HOM: hd->homunculus.class_ (status.cpp:8969, homunculus/data.rs)
        crate::map::BL_HOM => crate::homunculus::data::get(c, u.id).map(|hd| hd.homunculus.class_ as i32).unwrap_or(0),
        // BL_MER / BL_ELEM: mercenary.class_ / elemental.class_ (status.cpp:8970, 8972, companion.rs)
        crate::map::BL_MER | crate::map::BL_ELEM => crate::companion::get(c, u.id).map(|x| x.class()).unwrap_or(0),
        // pets (pet agent)
        _ => 0,
    }
}

/// status.cpp status_get_race2 (monsters: their database race groups; pets: pet agent).
pub fn race2_of(c: &Ctx, u: &Unit) -> Vec<u8> {
    if u.kind == BL_MOB {
        return super::mobif::race2(c, u);
    }
    Vec::new()
}

/// status.cpp status_get_lv.
pub fn lv_of(c: &Ctx, u: &Unit) -> i32 {
    crate::status::status_get_lv(c, u)
}

/// status.hpp status_get_class_ (CLASS_NORMAL / CLASS_BOSS …).
pub fn class_type(c: &Ctx, u: &Unit) -> i32 {
    status(c, u).class_ as i32
}

/// status.hpp status_bl_has_mode.
pub fn has_mode(c: &Ctx, u: &Unit, md: i32) -> bool {
    super::mobif::has_mode(status(c, u).mode, md)
}

/// status.cpp status_get_def: DEF, reduced while casting by the skill's CastDefenseReduction.
pub fn def_of(c: &Ctx, u: &Unit) -> i16 {
    use crate::status::defs::{DEFTYPE_MAX, DEFTYPE_MIN};
    let mut def = status(c, u).def as i32;
    if u.ud.skilltimer != 0 {
        def -= def * crate::db::skills::skill_get_castdef(u.ud.skill_id) / 100;
    }
    def.clamp(DEFTYPE_MIN as i32, DEFTYPE_MAX as i32) as i16
}

/// The item database entry of an equipped slot (sd->inventory_data[sd->equip_index[eqi]]), with the slot's item.
pub fn equip(c: &Ctx, sd: &Sd, eqi: usize) -> Option<(crate::inventory::InventoryItem, &'static crate::db::items::ItemData)> {
    let idx = *sd.it.equip_index.get(eqi)?;
    if idx < 0 {
        return None;
    }
    let row = crate::inventory::inv_get(c, sd.aid, idx as usize)?;
    let data = crate::inventory::data_of(&row.item);
    Some((row, data))
}

/// pc.cpp pc_checkskill_flag: the skill's e_skill_flag (SKILL_FLAG_PERMANENT 0 when not learned).
pub fn checkskill_flag(c: &Ctx, aid: u32, skill_id: u16) -> i32 {
    use crate::skill::char_skill as _;
    let Some(p) = crate::pc::get(c, aid) else { return 0 };
    c.db().char_skill().by_char().filter(p.char_id).find(|s| s.skill_id == skill_id).map(|s| s.flag as i32).unwrap_or(0)
}

/// pc.cpp pc_skillatk_bonus (bSkillAtk).
pub fn skillatk_bonus(sd: &Sd, skill_id: u16) -> i32 {
    let skill_id = crate::db::skills::skill_dummy2skill_id(skill_id);
    sd.st.lists.skillatk.iter().find(|it| it.id == skill_id).map(|it| it.val).unwrap_or(0)
}

/// pc.cpp pc_sub_skillatk_bonus (bSubSkill).
pub fn sub_skillatk_bonus(sd: &Sd, skill_id: u16) -> i32 {
    let skill_id = crate::db::skills::skill_dummy2skill_id(skill_id);
    sd.st.lists.subskill.iter().find(|it| it.id == skill_id).map(|it| it.val).unwrap_or(0)
}

/// map.cpp map_check_dir: 0 when `t_dir` is `s_dir` or one of its two neighbours (the unit faces that way), 1
/// otherwise.
pub fn map_check_dir(s_dir: i32, t_dir: i32) -> i32 {
    use crate::path::*;
    if s_dir == t_dir {
        return 0;
    }
    let (n, nw, w, sw, s, se, e, ne) = (DIR_NORTH as i32, DIR_NORTHWEST as i32, DIR_WEST as i32, DIR_SOUTHWEST as i32, DIR_SOUTH as i32, DIR_SOUTHEAST as i32, DIR_EAST as i32, DIR_NORTHEAST as i32);
    let near = match s_dir {
        x if x == n => t_dir == ne || t_dir == nw || t_dir == n,
        x if x == nw => t_dir == n || t_dir == w || t_dir == nw,
        x if x == w => t_dir == nw || t_dir == sw || t_dir == w,
        x if x == sw => t_dir == w || t_dir == s || t_dir == sw,
        x if x == s => t_dir == sw || t_dir == se || t_dir == s,
        x if x == se => t_dir == s || t_dir == e || t_dir == se,
        x if x == e => t_dir == se || t_dir == ne || t_dir == e,
        x if x == ne => t_dir == e || t_dir == n || t_dir == ne,
        _ => false,
    };
    if near {
        0
    } else {
        1
    }
}

/// pc.cpp pc_checkskill_summoner (the uint8 count of the Summoner's power skills).
pub fn checkskill_summoner(c: &Ctx, sd: &Sd, kind: i32) -> i32 {
    use crate::db::skills::*;
    use crate::status::defs::{SUMMONER_POWER_LAND, SUMMONER_POWER_LIFE, SUMMONER_POWER_SEA};
    let s = |id| checkskill(c, Some(sd), id);
    let count = match kind {
        SUMMONER_POWER_SEA => s(SU_TUNABELLY) + s(SU_TUNAPARTY) + s(SU_BUNCHOFSHRIMP) + s(SU_FRESHSHRIMP) + s(SU_GROOMING) + s(SU_PURRING) + s(SU_SHRIMPARTY),
        SUMMONER_POWER_LAND => s(SU_SV_STEMSPEAR) + s(SU_CN_POWDERING) + s(SU_CN_METEOR) + s(SU_SV_ROOTTWIST) + s(SU_CHATTERING) + s(SU_MEOWMEOW) + s(SU_NYANGGRASS),
        SUMMONER_POWER_LIFE => s(SU_SCAROFTAROU) + s(SU_PICKYPECK) + s(SU_ARCLOUSEDASH) + s(SU_LUNATICCARROTBEAT) + s(SU_HISS) + s(SU_POWEROFFLOCK) + s(SU_SVG_SPIRIT),
        _ => 0,
    };
    count as u8 as i32
}
