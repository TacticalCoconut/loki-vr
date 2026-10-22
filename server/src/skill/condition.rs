//! skill.cpp's skill conditions: skill_isNotOk (may the player use the skill here and now),
//! skill_check_condition_castbegin / skill_check_condition_castend (the per-skill requirements at cast begin and
//! end), skill_get_requirement (s_skill_condition after bonuses and states), skill_consume_requirement (HP / SP /
//! AP / zeny / spheres / items / ammo), skill_consume_hpspap, skill_isammotype, skill_check_condition_sc_required,
//! skill_check_bl_sc, skill_check_condition_mob_master_sub.
//!
//! Only the RENEWAL / RENEWAL_CAST paths of rAthena e985006 are ported; the pre-renewal case arms (`#ifndef RENEWAL`)
//! are named where they sit. The homunculus (sd->hd) is read from homunculus/data.rs; mercenaries and elementals
//! (sd->md / sd->ed: the mercelem agent's) never exist yet: the branches that read them are written as rAthena
//! answers without one.

use crate::battle::bl::sc_nonempty;
use crate::db::skills::*;
use crate::status::consts::*;
use crate::status::consts::SC_NONE;
use crate::status::data::ScEntry;
use crate::status::defs::{BF_WEAPON, ELE_GHOST, RINGNBL_SPCONSUM, SKILL_FLAG_PLAGIARIZED, W_BOW, W_FIST, W_GATLING, W_GRENADE, W_REVOLVER, W_RIFLE, W_SHOTGUN};
use crate::status::defs::{OPTION_CHASEWALK, OPTION_CLOAK, OPTION_DRAGON, OPTION_FALCON, OPTION_HIDE, OPTION_MADOGEAR, OPTION_RIDING, OPTION_WUG, OPTION_WUGRIDER};
use crate::status::sc::Sc;
use crate::unit::Unit;
use crate::world::Ctx;

use super::clif::fail::*;
use super::clif::{clif_skill_fail, clif_skill_teleportmessage, skill_fail, NOTIFY_MAPINFO_CANT_MEMO, NOTIFY_MAPINFO_CANT_TP, NOTIFY_MAPINFO_CANT_USE_SKILL};

/// skill.hpp struct s_skill_condition: one skill level's requirement after skill_get_requirement.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct SkillCondition {
    /// HP cost
    pub hp: i32,
    /// Max HP to trigger
    pub mhp: i32,
    /// SP cost
    pub sp: i32,
    /// AP cost
    pub ap: i32,
    /// HP cost (%)
    pub hp_rate: i32,
    /// SP cost (%)
    pub sp_rate: i32,
    /// AP cost (%)
    pub ap_rate: i32,
    /// Zeny cost
    pub zeny: i32,
    /// Weapon type. Combined bitmask of enum weapon_type (1<<weapon)
    pub weapon: i32,
    /// Ammo type. Combine bitmask of enum ammo_type (1<<ammo)
    pub ammo: i32,
    /// Amount of ammo
    pub ammo_qty: i32,
    /// State/condition. @see enum e_require_state
    pub state: i32,
    /// Spiritball cost
    pub spiritball: i32,
    /// Required item
    pub itemid: [u32; MAX_SKILL_ITEM_REQUIRE],
    /// Amount of item
    pub amount: [i32; MAX_SKILL_ITEM_REQUIRE],
    /// List of equipped item
    pub eq_item: Vec<u32>,
    /// List of Status required (SC)
    pub status: Vec<i32>,
}

/// clif.hpp MSI_IMPOSSIBLE_SKILL_AREA; color_table[COLOR_CYAN] (clif.cpp do_init_clif: 0x00FFFF stored as BGR).
const MSI_IMPOSSIBLE_SKILL_AREA: u16 = 1334;
const COLOR_CYAN: u32 = 0xFFFF00;
/// clif.hpp e_clif_messages: the "need an equipped gun / Saint bullet" messages (not script constants).
const MSI_FAIL_NEED_EQUIPPED_GUN_GATLING: u16 = 2554;
const MSI_FAIL_NEED_EQUIPPED_GUN_SHOTGUN: u16 = 2555;
const MSI_FAIL_NEED_EQUIPPED_GUN_RIFLE: u16 = 2556;
const MSI_FAIL_NEED_EQUIPPED_GUN_HANDGUN: u16 = 2557;
const MSI_FAIL_NEED_EQUIPPED_PROPERTY_SAINT_BULLET: u16 = 2558;
const MSI_FAIL_NEED_EQUIPPED_GUN_GRANADE: u16 = 2561;
/// map.hpp e_mapflag MF_NOBUYINGSTORE / MF_NOICEWALL (script constants mf_nobuyingstore / mf_noicewall).
const MF_NOBUYINGSTORE: i16 = 75;
const MF_NOICEWALL_: i16 = 15;

/// itemdb.hpp item ids the requirement code names.
pub const ITEMID_HOLY_WATER: u32 = 523;
pub const ITEMID_POISON_BOTTLE: u32 = 678;
pub const ITEMID_EMPTY_BOTTLE: u32 = 713;
pub const ITEMID_RED_GEMSTONE: u32 = 716;
pub const ITEMID_BLUE_GEMSTONE: u32 = 717;
pub const ITEMID_TRAP: u32 = 1065;
pub const ITEMID_PAINT_BRUSH: u32 = 6122;
pub const ITEMID_MAGIC_GEAR_FUEL: u32 = 6146;
pub const ITEMID_TRAP_ALLOY: u32 = 7940;
pub const ITEMID_ANCILLA: u32 = 12333;

/// itemdb.hpp e_item_group IG_GEMSTONE / IG_EDP (script constants).
pub const IG_GEMSTONE: u16 = 15;
pub const IG_EDP: u16 = 1586;

/// pc.hpp e_ammo_type MAX_AMMO_TYPE; itemdb.hpp AMMO_TYPE_ALL ((1 << MAX_AMMO_TYPE) - 1).
const MAX_AMMO_TYPE: i32 = 10;
pub const AMMO_TYPE_ALL: i32 = (1 << MAX_AMMO_TYPE) - 1;

/// mob.hpp enum MOBID (not script constants; checked against the monster database's AegisName in the tests).
const MOBID_MARINE_SPHERE: i32 = 1142;
const MOBID_G_PARASITE: i32 = 1555;
const MOBID_G_FLORA: i32 = 1575;
const MOBID_G_HYDRA: i32 = 1579;
const MOBID_G_MANDRAGORA: i32 = 1589;
const MOBID_G_GEOGRAPHER: i32 = 1590;
const MOBID_GUARDIAN_STONE1: i32 = 1907;
const MOBID_GUARDIAN_STONE2: i32 = 1908;
const MOBID_SILVERSNIPER: i32 = 2042;
const MOBID_MAGICDECOY_FIRE: i32 = 2043;
const MOBID_MAGICDECOY_WIND: i32 = 2046;
const MOBID_ZANZOU: i32 = 2308;
const MOBID_ABR_BATTLE_WARIOR: i32 = 20834;
const MOBID_ABR_DUAL_CANNON: i32 = 20835;
const MOBID_ABR_MOTHER_NET: i32 = 20836;
const MOBID_ABR_INFINITY: i32 = 20837;
const MOBID_BIONIC_WOODENWARRIOR: i32 = 20848;
const MOBID_BIONIC_WOODEN_FAIRY: i32 = 20849;
const MOBID_BIONIC_CREEPER: i32 = 20850;
const MOBID_BIONIC_HELLTREE: i32 = 20851;
const MOBID_PORING: i32 = crate::db::mobs::MOBID_PORING as i32;
const MOBID_EMPERIUM: i32 = crate::db::mobs::MOBID_EMPERIUM as i32;

// ---------------------------------------------------------------------------------------------- small helpers

/// `sc->getSCE(type)` on a status_change that is nullptr when empty (`if (sc->empty()) sc = nullptr`).
fn sce(sc: &Option<Sc>, t: i32) -> Option<&ScEntry> {
    sc.as_ref().and_then(|s| s.get(t))
}

/// `sc && sc->getSCE(type)`.
fn has(sc: &Option<Sc>, t: i32) -> bool {
    sce(sc, t).is_some()
}

/// pc.hpp pc_ishiding / pc_isfalcon / pc_isriding / pc_iswug / pc_isridingwug / pc_ismadogear / pc_isridingdragon:
/// the unit's sc.option bits.
fn pc_ishiding(u: &Unit) -> bool {
    u.sc.option & (OPTION_HIDE | OPTION_CLOAK | OPTION_CHASEWALK) != 0
}
fn pc_isfalcon(u: &Unit) -> bool {
    u.sc.option & OPTION_FALCON != 0
}
fn pc_isriding(u: &Unit) -> bool {
    u.sc.option & OPTION_RIDING != 0
}
fn pc_iswug(u: &Unit) -> bool {
    u.sc.option & OPTION_WUG != 0
}
fn pc_isridingwug(u: &Unit) -> bool {
    u.sc.option & OPTION_WUGRIDER != 0
}
fn pc_ismadogear(u: &Unit) -> bool {
    u.sc.option & OPTION_MADOGEAR != 0
}
fn pc_isridingdragon(u: &Unit) -> bool {
    u.sc.option & OPTION_DRAGON != 0
}

/// pc.hpp pc_check_weapontype (weapon check considering dual wielding): status2's port in status/sc_ext.rs.
fn pc_check_weapontype(c: &Ctx, aid: u32, type_: i32) -> bool {
    crate::status::sc_ext::pc_check_weapontype(c, aid, type_)
}

/// common/utils.cpp get_percentage: `(uint32)floor(100 * ((double)A / (double)B))`, ~0U when B is 0 (the monster
/// code's mob/spawn.rs version is an integer approximation).
fn get_percentage(a: u32, b: u32) -> u32 {
    if b == 0 {
        log::error!("get_percentage: divison by zero! (A={a},B={b})");
        return !0u32;
    }
    let result = 100.0 * (a as f64 / b as f64);
    if result > u32::MAX as f64 {
        log::error!("get_percentage: result percentage too high! (A={a},B={b},result={result})");
        return u32::MAX;
    }
    result.floor() as u32
}

/// itemdb.cpp ItemGroupDatabase::item_exists_pc: the inventory index of the first item of the group the player
/// carries (the group's entries in their stored order), -1 when none.
fn item_exists_pc(c: &Ctx, aid: u32, group_id: u16) -> i16 {
    let Some(group) = crate::db::items::group::item_group(group_id) else { return -1 };
    if group.random.is_empty() {
        return -1;
    }
    for random in &group.random {
        for it in &random.data {
            let item_position = crate::inventory::pc_search_inventory(c, aid, it.nameid);
            if item_position != -1 {
                return item_position;
            }
        }
    }
    -1
}

/// mob.cpp mob_count_sub with a list of monster ids: 1 for a monster whose id is listed (every unit when the list is
/// empty). rAthena reads the list with va_arg until a 0; skill_check_condition_castbegin's SR_CURSEDCIRCLE call passes
/// no terminating 0, so the slots after its three ids hold whatever the stack had (never a real monster id in
/// practice): the three ids are what can match.
fn mob_count_sub(c: &Ctx, bl: &Unit, mobids: &[i32]) -> i32 {
    if bl.kind == crate::map::BL_MOB && !mobids.is_empty() && mobids[0] != 0 {
        let mob_id = crate::mob::get(c, bl.id).map(|m| m.mob_id).unwrap_or(0);
        return mobids.contains(&mob_id) as i32;
    }
    // If not counting monsters, count all
    1 // backward compatibility
}

/// date.cpp date_get_dayofyear: localtime's tm_yday (the oracle's time zone, script/npc_run.rs).
fn date_get_dayofyear(c: &Ctx) -> i32 {
    crate::script::npc_run::localtime(c.tick().div_euclid(1000)).yday
}

/// pc.cpp sg_info[i].day_func: date.cpp is_day_of_sun / is_day_of_moon / is_day_of_star.
pub(crate) fn sg_day_func(c: &Ctx, i: usize) -> bool {
    let d = date_get_dayofyear(c) + 1;
    match i {
        0 => d % 2 == 0,
        1 => d % 2 == 1,
        2 => d % 5 == 0,
        _ => false,
    }
}

/// The GM shortcut of skill_check_condition_castbegin / castend (PC_PERM_SKILL_UNCONDITIONAL and not an item skill):
/// the arrow state and the "old" sphere counts are set as a successful check would.
fn unconditional_pass(c: &Ctx, aid: u32, skill_id: u16) {
    crate::battle::state::with(c, aid, |b| b.arrow_atk = skill_get_ammotype(skill_id) != 0); // Need to do arrow state check.
    super::state::with(c, aid, |s| {
        s.spiritball_old = s.spiritball; // Need to do Spiritball check.
        s.soulball_old = s.soulball; // Need to do Soulball check.
        s.servantball_old = s.servantball; // Need to do Servantball check.
        s.abyssball_old = s.abyssball; // Need to do Abyssball check.
    });
}

/// The `switch (sd.menuskill_id)` of skill_check_condition_castbegin / castend: false when the open skill menu
/// forbids the skill.
fn menuskill_allows(c: &Ctx, aid: u32, skill_id: u16) -> bool {
    let menuskill_id = crate::itemui::menu(c, aid).0;
    if menuskill_id == AM_PHARMACY as i32 {
        return !matches!(skill_id, AM_PHARMACY | AC_MAKINGARROW | BS_REPAIRWEAPON | AM_TWILIGHT1 | AM_TWILIGHT2 | AM_TWILIGHT3);
    }
    let menus = [GN_MIX_COOKING, GN_MAKEBOMB, GN_S_PHARMACY, GN_CHANGEMATERIAL, MT_M_MACHINE, BO_BIONIC_PHARMACY];
    if menus.iter().any(|&m| m as i32 == menuskill_id) && menuskill_id != skill_id as i32 {
        return false;
    }
    true
}

/// map.hpp mapdata_flag_vs2 / mapdata_flag_gvg2_no_te / mapdata_flag_gvg2_te.
fn mapflag_vs2(c: &Ctx, m: u16) -> bool {
    use crate::npc::*;
    let f = |flag| mapflag(c, m, flag);
    f(MF_PVP) || f(MF_GVG_DUNGEON) || f(MF_GVG) || f(MF_GVG_CASTLE) || f(MF_GVG_TE) || f(MF_GVG_TE_CASTLE) || f(MF_BATTLEGROUND)
}
fn mapflag_gvg2_no_te(c: &Ctx, m: u16) -> bool {
    use crate::npc::*;
    mapflag(c, m, MF_GVG) || mapflag(c, m, MF_GVG_CASTLE)
}
fn mapflag_gvg2_te(c: &Ctx, m: u16) -> bool {
    use crate::npc::*;
    mapflag(c, m, MF_GVG_TE) || mapflag(c, m, MF_GVG_TE_CASTLE)
}

// ---------------------------------------------------------------------------------------------- skill_isNotOk

/// skill.cpp skill_isNotOk: true when the player cannot use the skill here and now (map restrictions, the
/// attack-motion leniency, cooldowns, mounts, warp / teleport / memo / shop / wall restrictions).
pub fn skill_is_not_ok(c: &Ctx, sd: &Unit, skill_id: u16) -> bool {
    use crate::npc::*;
    let aid = sd.id;
    if crate::pc::has_permission(c, aid, crate::rules::PC_PERM_SKILL_UNCONDITIONAL) {
        return false; // can do any damn thing they want
    }
    let st = super::state::get(c, aid);
    if skill_id == AL_TELEPORT && st.skillitem == skill_id as i16 && st.skillitemlv > 2 {
        return false; // Teleport lv 3 bypasses this check.[Inkfish]
    }
    let m = sd.map;
    if mapflag(c, m, MF_NOSKILL) && skill_id != ALL_EQSWITCH && st.skillitem == 0 {
        return true; // Item skills bypass noskill
    }
    // Epoque: This code will compare the player's attack motion value which is influenced by ASPD before allowing a
    // skill to be cast. This is to prevent no-delay ACT files from spamming skills such as AC_DOUBLE which do not have
    // a skill delay and are not regarded in terms of attack motion.
    let amotion = crate::status::data::sd_get(c, aid).map(|s| s.battle_status.amotion as i64).unwrap_or(0);
    if !st.autocast && st.skillitem != skill_id as i16 && st.canskill_tick != 0 && c.tick() - st.canskill_tick < amotion * super::conf::skill_amotion_leniency() as i64 / 100 {
        // attempted to cast a skill before the attack motion has finished
        return true;
    }
    if super::cooldown::is_blocked(c, aid, skill_id) {
        clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_SKILLINTERVAL, 0, 0);
        return true;
    }
    // It has been confirmed on a official server (thanks to Yommy) that item-cast skills bypass all mapflag
    // restrictions. Also, without this check, an exploit where an item casting + healing (or any other kind buff)
    // isn't deleted after used on a restricted map
    if st.skillitem == skill_id as i16 && !st.skillitem_keep_requirement && st.abra_flag == 0 {
        return false;
    }
    let skill_nocast = skill_get_nocast(skill_id) as u32;
    let zone = mapflag_value(c, m, MF_RESTRICTED) as u32; // mapdata->zone
    // Check skill restrictions [Celest]
    if (skill_nocast & 1 != 0 && !mapflag_vs2(c, m))
        || (skill_nocast & 2 != 0 && mapflag(c, m, MF_PVP))
        || (skill_nocast & 4 != 0 && mapflag_gvg2_no_te(c, m))
        || (skill_nocast & 8 != 0 && mapflag(c, m, MF_BATTLEGROUND))
        || (skill_nocast & 16 != 0 && mapflag_gvg2_te(c, m)) // WOE:TE
        || (zone != 0 && skill_nocast & zone != 0 && mapflag(c, m, MF_RESTRICTED))
    {
        // This skill cannot be used within this area.
        crate::clif_items::msg_color(c, sd, MSI_IMPOSSIBLE_SKILL_AREA, COLOR_CYAN);
        return true;
    }
    if crate::status::sc::has_sce(c, aid, SC_ALL_RIDING) {
        return true; // You can't use skills while in the new mounts (The client doesn't let you, this is to make cheat-safe)
    }
    match skill_id {
        AL_WARP | RETURN_TO_ELDICASTES | ALL_GUARDIAN_RECALL | ECLAGE_RECALL | ALL_NIFLHEIM_RECALL | ALL_PRONTERA_RECALL | ALL_GLASTHEIM_RECALL | ALL_THANATOS_RECALL
        | ALL_LIGHTHALZEN_RECALL => {
            if mapflag(c, m, MF_NOWARP) {
                clif_skill_teleportmessage(c, sd, NOTIFY_MAPINFO_CANT_TP);
                return true;
            }
            return false;
        }
        AL_TELEPORT | NPC_FATALMENACE | SC_DIMENSIONDOOR | ALL_ODINS_RECALL | WE_CALLALLFAMILY => {
            if mapflag(c, m, MF_NOTELEPORT) {
                clif_skill_teleportmessage(c, sd, NOTIFY_MAPINFO_CANT_TP);
                return true;
            }
            return false; // gonna be checked in 'skill_castend_nodamage_id'
        }
        WE_CALLPARTNER | WE_CALLPARENT | WE_CALLBABY => {
            if mapflag(c, m, MF_NOMEMO) {
                clif_skill_teleportmessage(c, sd, NOTIFY_MAPINFO_CANT_MEMO);
                return true;
            }
        }
        MC_VENDING | ALL_BUYING_STORE => {
            // (two identical case arms in rAthena: MC_VENDING with MF_NOVENDING / CELL_CHKNOVENDING, ALL_BUYING_STORE
            // with MF_NOBUYINGSTORE / CELL_CHKNOBUYINGSTORE)
            let noflag = if skill_id == MC_VENDING { crate::inventory::MF_NOVENDING } else { MF_NOBUYINGSTORE };
            if mapflag(c, m, noflag) {
                crate::clif::displaymessage(c, sd, crate::data::msg_txt(276)); // "You can't open a shop on this map"
                skill_fail(c, sd, skill_id);
                return true;
            }
            let nocell = if skill_id == MC_VENDING { super::iface::map_getcell_novending(c, m, sd.x, sd.y) } else { super::iface::map_getcell_nobuyingstore(c, m, sd.x, sd.y) };
            if nocell {
                crate::clif::displaymessage(c, sd, crate::data::msg_txt(204)); // "You can't open a shop on this cell."
                skill_fail(c, sd, skill_id);
                return true;
            }
            if crate::chat::npc_isnear(c, sd) {
                // uncomment to send msg_txt.
                clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_THERE_ARE_NPC_AROUND, 0, 0);
                return true;
            }
        }
        MC_IDENTIFY => return false, // always allowed
        WZ_ICEWALL => {
            // noicewall flag [Valaris]
            if mapflag(c, m, MF_NOICEWALL_) {
                skill_fail(c, sd, skill_id);
                return true;
            }
        }
        GC_DARKILLUSION => {
            if mapflag_gvg2(c, m) {
                skill_fail(c, sd, skill_id);
                return true;
            }
        }
        GD_EMERGENCYCALL | GD_ITEMEMERGENCYCALL => {
            let ec = super::conf::emergency_call();
            if ec & (if crate::guild::is_agit_start(c) { 2 } else { 1 }) == 0
                || ec & (if mapflag_gvg2(c, m) { 8 } else { 4 }) == 0
                || (ec & 16 != 0 && mapflag(c, m, MF_NOWARPTO) && !(mapflag(c, m, MF_GVG_CASTLE) || mapflag(c, m, MF_GVG_TE_CASTLE)))
            {
                skill_fail(c, sd, skill_id);
                return true;
            }
        }
        WM_SIRCLEOFNATURE | WM_SOUND_OF_DESTRUCTION | WM_LULLABY_DEEPSLEEP | WM_GLOOMYDAY | WM_SATURDAY_NIGHT_FEVER => {
            if !mapflag_vs(c, m) {
                clif_skill_teleportmessage(c, sd, NOTIFY_MAPINFO_CANT_USE_SKILL); // This skill cannot be used in this area
                return true;
            }
        }
        _ => {}
    }
    false
}

// ---------------------------------------------------------------------------------------------- small checks

/// skill.cpp skill_check_condition_mob_master_sub (map_foreachinmap over BL_MOB): 1 for a monster summoned by
/// `src_id` with the AI of the skill (`*c` counts the ones of class `mob_class`), 0 otherwise.
pub fn skill_check_condition_mob_master_sub(c: &Ctx, bl: &Unit, src_id: u32, mob_class: i32, skill: u16, count: &mut i32) -> i32 {
    use crate::battle::mobif::{AI_ABR, AI_BIONIC, AI_FAW, AI_FLORA, AI_LEGION, AI_SPHERE, AI_ZANZOU};
    let Some(md) = crate::mob::get(c, bl.id) else { return 0 };
    let ai = match skill {
        AM_SPHEREMINE => AI_SPHERE,
        AM_CANNIBALIZE => AI_FLORA,
        KO_ZANZOU => AI_ZANZOU,
        MH_SUMMON_LEGION => AI_LEGION,
        NC_SILVERSNIPER | NC_MAGICDECOY => AI_FAW,
        MT_SUMMON_ABR_BATTLE_WARIOR | MT_SUMMON_ABR_DUAL_CANNON | MT_SUMMON_ABR_MOTHER_NET | MT_SUMMON_ABR_INFINITY => AI_ABR,
        BO_WOODENWARRIOR | BO_WOODEN_FAIRY | BO_CREEPER | BO_HELLTREE => AI_BIONIC,
        _ => AI_FLORA,
    };
    if md.master_id != src_id || md.special_ai as i32 != ai {
        return 0; // Non alchemist summoned mobs have nothing to do here.
    }
    if md.mob_id == mob_class {
        *count += 1;
    }
    1
}

/// map_foreachinmap(skill_check_condition_mob_master_sub, m, BL_MOB, src_id, mob_class, skill, &c): the number of
/// callbacks that returned 1.
fn count_mob_master(c: &Ctx, m: u16, src_id: u32, mob_class: i32, skill: u16, count: &mut i32) -> i32 {
    let mut n = 0;
    for bl in crate::mob::mobs_foreachinmap(c, m) {
        n += skill_check_condition_mob_master_sub(c, &bl, src_id, mob_class, skill, count);
    }
    n
}

/// skill.cpp skill_isammotype: the skill uses the weapon and consumes ammo (arrow_decrement 2 only). [Skotlex]
pub fn skill_isammotype(c: &Ctx, aid: u32, skill_id: u16) -> bool {
    let weapon = crate::pc::character_of(c, aid).map(|ch| ch.weapon as i32).unwrap_or(0);
    crate::battle::conf::arrow_decrement() == 2
        && (weapon == W_BOW || (W_REVOLVER..=W_GRENADE).contains(&weapon))
        && skill_id != HT_PHANTASMIC
        // (rAthena dereferences skill_db.find's result: an unknown skill id never gets here in practice)
        && skill_db().find(skill_id).map(|s| s.skill_type as i32 == BF_WEAPON && !s.has_nk(NK_NODAMAGE)).unwrap_or(false)
        && skill_get_spiritball(skill_id, 1) == 0 // Assume spirit spheres are used as ammo instead.
}

/// skill.cpp skill_check_condition_sc_required: every status of `require.status` must be active (the official fail
/// message for the cart, Poisoning Weapon and Weapon Blocking).
pub fn skill_check_condition_sc_required(c: &Ctx, sd: &Unit, skill_id: u16, require: &SkillCondition) -> bool {
    if require.status.is_empty() {
        return true;
    }
    // (sd.sc is never nullptr)
    let sc = crate::status::sc::load(c, sd.id);
    // May have multiple requirements
    for &req_status in &require.status {
        if req_status == SC_NONE {
            continue;
        }
        let cause = match req_status {
            // Official fail message
            SC_PUSH_CART => USESKILL_FAIL_CART,
            SC_POISONINGWEAPON => USESKILL_FAIL_GC_POISONINGWEAPON,
            SC_WEAPONBLOCK_ON => USESKILL_FAIL_GC_WEAPONBLOCKING,
            _ => USESKILL_FAIL_LEVEL,
        };
        if !sc.has(req_status) {
            clif_skill_fail(c, sd, skill_id, cause, 0, 0);
            return false;
        }
    }
    true
}

/// skill.cpp skill_check_bl_sc (a map_foreach callback): 1 when the target has status `sc_id`.
pub fn skill_check_bl_sc(c: &Ctx, target: &Unit, sc_id: i32) -> i32 {
    match crate::battle::bl::sc(c, target) {
        Some(s) if s.has(sc_id) => 1,
        _ => 0,
    }
}

/// skill.cpp skill_consume_hpspap: status_zap of the costs, except the HP of the skills that require HP without
/// consuming it.
pub fn skill_consume_hpspap(c: &Ctx, bl: &Unit, skill_id: u16, hp: i32, sp: i32, ap: i32) {
    let mut hp = hp;
    match skill_id {
        // Skills that require HP but do not consume them
        SM_MAGNUM | MS_MAGNUM => hp = 0,
        _ => {}
    }
    crate::status::hp::status_zap(c, bl.id, hp as i64, sp as i64, ap as i64);
}

// ---------------------------------------------------------------------------------------------- cast begin

/// skill.cpp skill_check_condition_castbegin: the checks when a player starts a skill (ammo only by whether the skill
/// needs it; its type and amount are checked at cast end). True when every condition passed.
pub fn skill_check_condition_castbegin(c: &Ctx, sd: &Unit, skill_id: u16, skill_lv: u16) -> bool {
    use crate::inventory::{data_of, inv_get, pc_search_inventory};
    let aid = sd.id;
    let mut skill_lv = skill_lv;
    if crate::pc::get(c, aid).map(|p| p.chat_id != 0).unwrap_or(false) {
        return false;
    }
    let st = super::state::get(c, aid);
    if crate::pc::has_permission(c, aid, crate::rules::PC_PERM_SKILL_UNCONDITIONAL) && st.skillitem != skill_id as i16 {
        // GMs don't override the skillItem check, otherwise they can use items without them being consumed! [Skotlex]
        unconditional_pass(c, aid, skill_id);
        return true;
    }
    if !menuskill_allows(c, aid, skill_id) {
        return false;
    }
    let sc = sc_nonempty(c, sd);
    if st.skillitem == skill_id as i16 {
        if st.abra_flag != 0 {
            // Hocus-Pocus was used. [Inkfish]
            super::state::with(c, aid, |s| s.abra_flag = 0);
        } else {
            // When a target was selected, consume items that were skipped in pc_use_item [Skotlex]
            let Some(mut pi) = crate::inventory::st(c, aid) else { return false };
            let i = pi.itemindex;
            let row = if i < 0 { None } else { inv_get(c, aid, i as usize) };
            let ok = row.as_ref().map(|r| r.item.nameid == pi.itemid && data_of(&r.item).flag.delay_consume != crate::db::items::DELAYCONSUME_NONE && r.item.amount >= 1).unwrap_or(false);
            pi.itemid = 0;
            pi.itemindex = -1;
            crate::inventory::put_st(c, &pi);
            let Some(row) = row.filter(|_| ok) else {
                // Something went wrong, item exploit?
                return false;
            };
            // Consume
            let earthscroll = skill_id == WZ_EARTHSPIKE && sce(&sc, SC_EARTHSCROLL).map(|e| (c.rnd() % 100) as i32 > e.val2).unwrap_or(false);
            if earthscroll || data_of(&row.item).flag.delay_consume & crate::db::items::DELAYCONSUME_NOCONSUME != 0 {
                // [marquis007] Do not consume item.
            } else if row.item.expire_time == 0 {
                crate::inventory::pc_delitem(c, aid, i as i32, 1, 0, 0, crate::map_log::LOG_TYPE_CONSUME); // Rental usable items are not consumed until expiration
            }
        }
        if !st.skillitem_keep_requirement {
            return true;
        }
    }
    if has(&sc, SC_WEIGHT90) {
        clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_WEIGHTOVER, 0, 0);
        return false;
    }
    if has(&sc, SC__SHADOWFORM) || has(&sc, SC__IGNORANCE) {
        return false;
    }
    // Checks if disabling skill - in which case no SP requirements are necessary
    if sc.is_some() && super::misc::skill_disable_check(c, aid, skill_id) {
        return true;
    }
    // (rAthena dereferences skill_db.find's result: callers only pass skills of the database)
    let Some(skill) = skill_db().find(skill_id) else { return false };
    let u = crate::unit::get(c, aid).unwrap_or_else(|| sd.clone());
    // Check the skills that can be used while mounted on a warg
    if pc_isridingwug(&u) && !skill.has_inf2(INF2_ALLOWONWARG) {
        return false; // in official there is no message.
    }
    if pc_ismadogear(&u) {
        // Skills that are unusable when Mado is equipped. [Jobbie]
        if !skill.has_inf2(INF2_ALLOWONMADO) {
            clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_MADOGEAR_RIDE, 0, 0);
            return false;
        }
    }
    let mut require = skill_get_requirement(c, &u, skill_id, skill_lv);
    // Can only update state when weapon/arrow info is checked.
    let arrow_atk = require.ammo != 0;
    crate::battle::state::with(c, aid, |b| b.arrow_atk = arrow_atk);
    // perform skill-group checks
    if skill_id != WM_GREAT_ECHO && skill.has_inf2(INF2_ISCHORUS) {
        if super::iface::skill_check_pc_partner(c, &u, skill_id, &mut skill_lv, crate::conf::area_size() as i32, 0) < 1 && !has(&sc, SC_KVASIR_SONATA) {
            skill_fail(c, sd, skill_id);
            return false;
        }
    } else if skill.has_inf2(INF2_ISENSEMBLE) && super::iface::skill_check_pc_partner(c, &u, skill_id, &mut skill_lv, 1, 0) < 1 && !has(&sc, SC_KVASIR_SONATA) {
        skill_fail(c, sd, skill_id);
        return false;
    }
    let st = super::state::get(c, aid);
    let pst = crate::status::data::sd_get(c, aid);
    let class_ = pst.as_ref().map(|p| p.class_).unwrap_or(0);
    let status = pst.as_ref().map(|p| p.battle_status.clone()).unwrap_or_default();
    let ch = crate::pc::character_of(c, aid);
    let items = crate::inventory::st(c, aid);
    let fail = |cause: u8| clif_skill_fail(c, sd, skill_id, cause, 0, 0);
    // perform skill-specific checks (and actions)
    match skill_id {
        RG_GRAFFITI => {
            if super::iface::skill_graffiti_count(c, u.map) != 0 {
                // If a previous Graffiti exists skill fails to cast.
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        SO_SPELLFIST | SA_CASTCANCEL => {
            if skill_id == SO_SPELLFIST && st.skill_id_old != MG_FIREBOLT && st.skill_id_old != MG_COLDBOLT && st.skill_id_old != MG_LIGHTNINGBOLT {
                skill_fail(c, sd, skill_id);
                return false;
            }
            // [[fallthrough]] SA_CASTCANCEL
            if u.ud.skilltimer == 0 {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        AS_CLOAKING => {
            // (sd is a player: pc_cloak_check_type; monster_cloak_check_type is for sd.type != BL_PC)
            let check = if u.kind == crate::map::BL_PC { super::conf::pc_cloak_check_type() & 1 != 0 } else { super::conf::monster_cloak_check_type() & 1 != 0 };
            if skill_lv < 3 && check {
                // Check for walls.
                use crate::path::{DIRX, DIRY, DIR_MAX};
                let dir = (0..DIR_MAX as usize).find(|&d| crate::map_cell::getcell(c, Some(u.map), u.x + DIRX[d], u.y + DIRY[d], crate::map_cell::CELL_CHKNOPASS) != 0);
                if dir.is_none() {
                    skill_fail(c, sd, skill_id);
                    return false;
                }
            }
        }
        AL_WARP => {
            if !super::conf::duel_allow_teleport() && super::iface::duel_group(c, aid) != 0 {
                // duel restriction [LuzZza]
                let output = crate::data::msg_txt(365).replacen("%s", skill_get_name(AL_WARP), 1);
                crate::clif::displaymessage(c, sd, &output); // "Duel: Can't use %s in duel."
                return false;
            }
        }
        AL_HOLYWATER => {
            if pc_search_inventory(c, aid, ITEMID_EMPTY_BOTTLE) < 0 {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        MO_CALLSPIRITS => {
            if let Some(e) = sce(&sc, SC_RAISINGDRAGON) {
                skill_lv = skill_lv.wrapping_add(e.val1 as u16);
            }
            if st.spiritball as i32 >= skill_lv as i32 {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        MO_FINGEROFFENSIVE | GS_FLING | SR_RIDEINLIGHTNING => {
            let old = if st.spiritball > 0 && (st.spiritball as i32) < require.spiritball {
                require.spiritball = st.spiritball as i32;
                st.spiritball
            } else {
                require.spiritball as i8
            };
            super::state::with(c, aid, |s| s.spiritball_old = old);
        }
        MO_CHAINCOMBO => {
            if sc.is_none() {
                return false;
            }
            if has(&sc, SC_BLADESTOP) {
                // break
            } else if sce(&sc, SC_COMBO).map(|e| e.val1 == MO_TRIPLEATTACK as i32).unwrap_or(false) {
                // break
            } else {
                return false;
            }
        }
        MO_COMBOFINISH => {
            if !sce(&sc, SC_COMBO).map(|e| e.val1 == MO_CHAINCOMBO as i32).unwrap_or(false) {
                return false;
            }
        }
        CH_TIGERFIST => {
            let Some(combo) = sce(&sc, SC_COMBO) else { return false };
            // In Renewal Tiger Fist can only be used after Combo Finish (RENEWAL; pre-renewal also after Chain Crush)
            if combo.val1 != MO_COMBOFINISH as i32 {
                return false;
            }
        }
        CH_CHAINCRUSH => {
            let Some(combo) = sce(&sc, SC_COMBO) else { return false };
            if combo.val1 != MO_COMBOFINISH as i32 && combo.val1 != CH_TIGERFIST as i32 {
                return false;
            }
        }
        SJ_SOLARBURST => {
            if !sce(&sc, SC_COMBO).map(|e| e.val1 == SJ_PROMINENCEKICK as i32).unwrap_or(false) {
                return false;
            }
        }
        MO_EXTREMITYFIST => {
            // if(sc && sc->getSCE(SC_EXTREMITYFIST)) //To disable Asura during the 5 min skill block uncomment this...
            //     return false;
            if has(&sc, SC_BLADESTOP) || has(&sc, SC_CURSEDCIRCLE_ATKER) {
                // break
            } else {
                if let Some(combo) = sce(&sc, SC_COMBO) {
                    if combo.val1 != MO_COMBOFINISH as i32 && combo.val1 != CH_CHAINCRUSH as i32 {
                        return false;
                    }
                } else if !crate::mob::unit::unit_can_move(c, &u) {
                    // Placed here as ST_MOVE_ENABLE should not apply if rooted or on a combo. [Skotlex]
                    skill_fail(c, sd, skill_id);
                    return false;
                }
                super::state::with(c, aid, |s| s.spiritball_old = s.spiritball);
            }
        }
        TK_MISSION => {
            use crate::db::jobs::{MAPID_SECONDMASK, MAPID_TAEKWON};
            if class_ & MAPID_SECONDMASK != MAPID_TAEKWON {
                // Cannot be used by Non-Taekwon classes
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ASC_EDP => {
            // (RENEWAL: switch_remove_edp & 2; pre-renewal & 1)
            if items.as_ref().map(|s| s.weapontype1 as i32 == W_FIST).unwrap_or(true) && super::conf::switch_remove_edp() & 2 != 0 {
                fail(USESKILL_FAIL_THIS_WEAPON);
                return false;
            }
        }
        TK_READYCOUNTER | TK_READYDOWN | TK_READYSTORM | TK_READYTURN | TK_JUMPKICK => {
            use crate::db::jobs::{MAPID_SECONDMASK, MAPID_SOUL_LINKER};
            if class_ & MAPID_SECONDMASK == MAPID_SOUL_LINKER {
                // Soul Linkers cannot use this skill
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        TK_TURNKICK | TK_STORMKICK | TK_DOWNKICK | TK_COUNTER => {
            use crate::db::jobs::{MAPID_SECONDMASK, MAPID_SOUL_LINKER};
            if class_ & MAPID_SECONDMASK == MAPID_SOUL_LINKER {
                return false; // Anti-Soul Linker check in case you job-changed with Stances active.
            }
            let Some(combo) = sce(&sc, SC_COMBO) else { return false };
            if combo.val1 == TK_JUMPKICK as i32 {
                return false; // Combo needs to be ready
            }
            if combo.val3 != 0 {
                // Kick chain
                // Do not repeat a kick.
                if combo.val3 != skill_id as i32 {
                    // break
                } else {
                    crate::status::sc::status_change_end(c, aid, SC_COMBO, 0);
                    return false;
                }
            } else if combo.val1 != skill_id as i32 && !ch.as_ref().map(|ch| crate::skilltree::pc_is_taekwon_ranker(c, class_, ch)).unwrap_or(false) {
                // Cancel combo wait.
                super::unit::unit_cancel_combo(c, aid);
                return false;
            }
            // Combo ready.
        }
        // BD_ADAPTATION: pre-renewal only (#ifndef RENEWAL)
        PR_BENEDICTIO => {
            if super::iface::skill_check_pc_partner(c, &u, skill_id, &mut skill_lv, 1, 0) < 2 {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        SL_SMA => {
            if sc.is_none() || !(has(&sc, SC_SMA) || has(&sc, SC_USE_SKILL_SP_SHA)) {
                return false;
            }
        }
        HT_POWER => {
            if !sce(&sc, SC_COMBO).map(|e| e.val1 == AC_DOUBLE as i32).unwrap_or(false) {
                return false;
            }
        }
        // CG_HERMODE: pre-renewal only (#ifndef RENEWAL)
        CG_MOONLIT => {
            // Check there's no wall in the range+1 area around the caster. [Skotlex]
            let range = skill_get_splash(skill_id, skill_lv) + 1;
            let size = range * 2 + 1;
            for s in 0..size * size {
                let x = u.x as i32 + (s % size - range);
                let y = u.y as i32 + (s / size - range);
                if crate::map_cell::getcell(c, Some(u.map), x as i16, y as i16, crate::map_cell::CELL_CHKWALL) != 0 {
                    skill_fail(c, sd, skill_id);
                    return false;
                }
            }
        }
        // PR_REDEMPTIO, HP_BASILICA: pre-renewal only (#ifndef RENEWAL)
        AM_TWILIGHT2 | AM_TWILIGHT3 => {
            let party_id = ch.as_ref().map(|ch| ch.party_id).unwrap_or(0);
            if super::misc::party_skill_check(c, aid, party_id, skill_id, skill_lv) == 0 {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        SG_SUN_COMFORT | SG_MOON_COMFORT | SG_STAR_COMFORT => {
            if has(&sc, SC_MIRACLE) {
                // break
            } else {
                let i = (skill_id - SG_SUN_COMFORT) as usize;
                let feel = super::iface::sg_feel_map(c, aid, i);
                if !(feel == Some(u.map) && (crate::mob::conf::allow_skill_without_day() || sg_day_func(c, i))) {
                    skill_fail(c, sd, skill_id);
                    return false;
                }
            }
        }
        SG_FUSION => {
            if sce(&sc, SC_SPIRIT).map(|e| e.val2 == SL_STAR as i32).unwrap_or(false) {
                // break
            } else {
                // Auron insists we should implement SP consumption when you are not Soul Linked. [Skotlex]
                // Only invoke on skill begin cast (instant cast skill). [Kevin]
                if require.sp > 0 {
                    if status.sp < require.sp as u32 {
                        fail(USESKILL_FAIL_SP_INSUFFICIENT);
                    } else {
                        crate::status::hp::status_zap(c, aid, 0, require.sp as i64, 0);
                    }
                }
                return false;
            }
        }
        GD_BATTLEORDER | GD_REGENERATION | GD_RESTORE | GD_CHARGESHOUT_FLAG | GD_CHARGESHOUT_BEATING | GD_EMERGENCY_MOVE | GD_EMERGENCYCALL | GD_ITEMEMERGENCYCALL => {
            if !matches!(skill_id, GD_EMERGENCYCALL | GD_ITEMEMERGENCYCALL) && !crate::npc::mapflag_gvg2(c, u.map) {
                skill_fail(c, sd, skill_id);
                return false;
            }
            // [[fallthrough]] GD_EMERGENCYCALL / GD_ITEMEMERGENCYCALL: other checks were already done in skill_isNotOk()
            let guild_id = ch.as_ref().map(|ch| ch.guild_id).unwrap_or(0);
            if guild_id == 0 || (!crate::guild::gx(c, aid).gmaster_flag && skill_id != GD_CHARGESHOUT_BEATING) {
                return false;
            }
        }
        GS_GLITTERING | RL_RICHS_COIN => {
            if st.spiritball >= 10 {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        NJ_ISSEN | NJ_BUNSINJYUTSU => {
            // RENEWAL: `status->hp < (status->hp / 100)` (pre-renewal: hp < 2)
            if skill_id == NJ_ISSEN && status.hp < status.hp / 100 {
                skill_fail(c, sd, skill_id);
                return false;
            }
            // [[fallthrough]] NJ_BUNSINJYUTSU
            if !has(&sc, SC_NEN) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        NJ_ZENYNAGE | KO_MUCHANAGE => {
            let zeny = ch.as_ref().map(|ch| ch.zeny as i64).unwrap_or(0);
            if zeny < require.zeny as i64 {
                fail(USESKILL_FAIL_MONEY);
                return false;
            }
        }
        PF_HPCONVERSION => {
            if status.sp == status.max_sp {
                return false; // Unusable when at full SP.
            }
        }
        SP_KAUTE => {
            // Fail if below 30% MaxHP.
            if status.hp < 30u32.wrapping_mul(status.max_hp) / 100 {
                fail(USESKILL_FAIL);
                return false;
            }
        }
        AM_CALLHOMUN => {
            // Can't summon if a hom is already out (sd.status.hom_id && sd.hd && !sd.hd->homunculus.vaporize)
            let hd = crate::homunculus::data::of_master(c, aid);
            if crate::homunculus::hom_id_of(c, aid) != 0 && hd.map(|hd| hd.homunculus.vaporize == 0).unwrap_or(false) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        AM_REST => {
            // Can't vapo homun if you don't have an active homunc or it's hp is < 80%
            let hd = crate::homunculus::data::of_master(c, aid);
            let low = hd.as_ref().map(|hd| hd.battle_status.hp < hd.battle_status.max_hp.wrapping_mul(80) / 100).unwrap_or(false);
            if !crate::homunculus::data::hom_is_active(hd.as_ref()) || low {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        AB_ANCILLA => {
            let count: i32 = crate::inventory::inv_list(c, aid).iter().filter(|r| (r.idx as usize) < crate::inventory::MAX_INVENTORY && r.item.nameid == ITEMID_ANCILLA).map(|r| r.item.amount as i32).sum();
            if count >= 3 {
                fail(USESKILL_FAIL_ANCILLA_NUMOVER);
                return false;
            }
        }
        AB_ADORAMUS => {
            // bugreport:7647 mistress card DOES remove requirements for gemstones from Adoramus -helvetica
            let no_gemstone = pst.as_ref().map(|p| p.special_state.no_gemstone).unwrap_or(0);
            if super::iface::skill_check_pc_partner(c, &u, skill_id, &mut skill_lv, 1, 0) <= 0 && require.itemid[0] != 0 && no_gemstone == 0 && {
                let i = pc_search_inventory(c, aid, require.itemid[0]);
                i < 0 || inv_get(c, aid, i as usize).map(|r| (r.item.amount as i32) < require.amount[0]).unwrap_or(true)
            } {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        WL_SUMMONFB | WL_SUMMONBL | WL_SUMMONWB | WL_SUMMONSTONE => {
            if skill_lv == 1 && sc.is_some() {
                // Failure only happens on level 1
                let free = (SC_SPHERE_1..=SC_SPHERE_5).any(|t| !has(&sc, t));
                if !free {
                    // No more free slots
                    fail(USESKILL_FAIL_SUMMON);
                    return false;
                }
            }
        }
        WL_TETRAVORTEX | WL_RELEASE => {
            // bugreport:7598 moved sphere check to precast to avoid triggering cooldown per official behavior -helvetica
            let active_spheres = (SC_SPHERE_1..=SC_SPHERE_5).filter(|&t| has(&sc, t)).count() as i32;
            // Cast requirement
            let req_spheres = if skill_id == WL_TETRAVORTEX {
                4
            } else if skill_id == WL_RELEASE && skill_lv == 2 {
                1 // Only level 2 uses Spheres
            } else {
                0
            };
            if active_spheres < req_spheres {
                // Need minimum amount of spheres
                fail(if skill_id == WL_RELEASE { USESKILL_FAIL_SUMMON_NONE } else { USESKILL_FAIL_LEVEL });
                return false;
            }
        }
        GC_HALLUCINATIONWALK => {
            if has(&sc, SC_HALLUCINATIONWALK) || has(&sc, SC_HALLUCINATIONWALK_POSTDELAY) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        NPC_HALLUCINATIONWALK => {
            if has(&sc, SC_NPC_HALLUCINATIONWALK) {
                return false;
            }
        }
        RA_WUGMASTERY => {
            let hawk = crate::skill::checkskill(c, aid, WH_HAWK_M) != 0;
            if (pc_isfalcon(&u) && (!hawk && !super::conf::warg_can_falcon())) || pc_isridingwug(&u) || crate::status::sc::has_sce(c, aid, SC__GROOMY) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        RA_WUGSTRIKE => {
            if !pc_iswug(&u) && !pc_isridingwug(&u) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        RA_WUGRIDER => {
            let hawk = crate::skill::checkskill(c, aid, WH_HAWK_M) != 0;
            if (pc_isfalcon(&u) && (!hawk && !super::conf::warg_can_falcon())) || (!pc_isridingwug(&u) && !pc_iswug(&u)) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        RA_WUGDASH => {
            if !pc_isridingwug(&u) {
                skill_fail(c, sd, skill_id);
                return false;
            }
            use crate::path::*;
            let (mut sx, mut sy) = (u.x, u.y);
            let dir = crate::unit::getdir(&u) % DIR_MAX;
            match dir {
                DIR_NORTH => sy += 1,
                DIR_NORTHWEST => {
                    sx -= 1;
                    sy += 1;
                }
                DIR_WEST => sx -= 1,
                DIR_SOUTHWEST => {
                    sx -= 1;
                    sy -= 1;
                }
                DIR_SOUTH => sy -= 1,
                DIR_SOUTHEAST => {
                    sx += 1;
                    sy -= 1;
                }
                DIR_EAST => sx += 1,
                DIR_NORTHEAST => {
                    sx += 1;
                    sy += 1;
                }
                _ => {}
            }
            if crate::map::count_oncell(c, u.map, sx, sy, crate::map::BL_CHAR, 1) > 0 {
                return false;
            }
        }
        LG_RAYOFGENESIS | LG_BANDING => {
            if has(&sc, SC_INSPIRATION) {
                return true; // Don't check for partner.
            }
        }
        LG_PRESTIGE => {
            if has(&sc, SC_INSPIRATION) {
                return true; // Don't check for partner.
            }
            if has(&sc, SC_BANDING) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        LG_RAGEBURST => {
            if st.spiritball == 0 {
                fail(USESKILL_FAIL_SKILLINTERVAL);
                return false;
            }
            require.spiritball = st.spiritball as i32;
            super::state::with(c, aid, |s| s.spiritball_old = st.spiritball);
        }
        SR_FALLENEMPIRE => {
            if !sce(&sc, SC_COMBO).map(|e| e.val1 == SR_DRAGONCOMBO as i32).unwrap_or(false) {
                return false;
            }
        }
        SR_CRESCENTELBOW => {
            if has(&sc, SC_CRESCENTELBOW) {
                fail(USESKILL_FAIL_DUPLICATE);
                return false;
            }
        }
        SR_CURSEDCIRCLE => {
            if crate::npc::mapflag_gvg2(c, u.map) {
                let range = skill_get_splash(skill_id, skill_lv) as i16;
                let near = super::area::foreach_in_range(c, &u, range, crate::map::BL_MOB, super::area::Wall::None, |c, bl| mob_count_sub(c, bl, &[MOBID_EMPERIUM, MOBID_GUARDIAN_STONE1, MOBID_GUARDIAN_STONE2]));
                if near != 0 {
                    // You're too close to a stone or emperium to use this skill.
                    let output = crate::net::wire::latin1(crate::data::msg_txt(382));
                    crate::clif::messagecolor_target(c, sd, crate::social::COLOR_RED, &output, false, crate::clif::Target::SelfOnly, None);
                    return false;
                }
            }
            if st.spiritball > 0 {
                require.spiritball = st.spiritball as i32;
                super::state::with(c, aid, |s| s.spiritball_old = st.spiritball);
            } else {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        SR_GATEOFHELL => {
            if st.spiritball > 0 {
                let old = require.spiritball as i8;
                super::state::with(c, aid, |s| s.spiritball_old = old);
            }
        }
        SC_MANHOLE | SC_DIMENSIONDOOR => {
            if has(&sc, SC_MAGNETICFIELD) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        SC_FEINTBOMB => {
            if super::iface::map_getcell_landprotector(c, u.map, u.x, u.y) || super::iface::map_getcell_maelstrom(c, u.map, u.x, u.y) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        WM_GREAT_ECHO => {
            let count = super::iface::skill_check_pc_partner(c, &u, skill_id, &mut skill_lv, crate::conf::area_size() as i32, 1);
            if count > 0 {
                require.sp -= require.sp * 20 * count / 100; // -20% each W/M in the party.
            }
        }
        SO_FIREWALK | SO_ELECTRICWALK | NPC_FIREWALK | NPC_ELECTRICWALK => {
            // Can't be casted until you've walked all cells.
            if let Some(e) = sce(&sc, SC_PROPERTYWALK) {
                if e.val3 < skill_get_maxcount(e.val1 as u16, e.val2 as u16) {
                    skill_fail(c, sd, skill_id);
                    return false;
                }
            }
        }
        SO_EL_CONTROL => {
            // `!sd.status.ele_id || !sd.ed` (elemental/data.rs)
            if crate::elemental::data::ele_id_of(c, sd.id) == 0 || crate::elemental::data::of_master(c, sd.id).is_none() {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        KO_JYUMONJIKIRI => {
            let (w1, w2) = items.as_ref().map(|s| (s.weapontype1 as i32, s.weapontype2 as i32)).unwrap_or((W_FIST, W_FIST));
            let shield = ch.as_ref().map(|ch| ch.shield as i32).unwrap_or(0);
            if w1 != W_FIST && (w2 != W_FIST || shield != W_FIST) {
                return true;
            }
            skill_fail(c, sd, skill_id);
            return false;
        }
        KO_KAHU_ENTEN | KO_HYOUHU_HUBUKI | KO_KAZEHU_SEIRAN | KO_DOHU_KOUKAI => {
            if st.spiritcharm_type == skill_get_ele(skill_id, skill_lv) && st.spiritcharm as i32 >= super::state::MAX_SPIRITCHARM {
                fail(USESKILL_FAIL_SUMMON);
                return false;
            }
        }
        KO_KAIHOU | KO_ZENKAI => {
            if st.spiritcharm_type == super::state::CHARM_TYPE_NONE || st.spiritcharm <= 0 {
                fail(USESKILL_FAIL_SUMMON_NONE);
                return false;
            }
        }
        SS_FOUR_CHARM => {
            if st.spiritcharm_type == super::state::CHARM_TYPE_NONE || (st.spiritcharm as i32) < super::state::MAX_SPIRITCHARM {
                fail(USESKILL_FAIL_SUMMON_NONE);
                return false;
            }
        }
        SJ_FULLMOONKICK => {
            if !has(&sc, SC_NEWMOON) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        SJ_STAREMPEROR | SJ_NOVAEXPLOSING | SJ_GRAVITYCONTROL | SJ_BOOKOFDIMENSION | SJ_BOOKOFCREATINGSTAR | SP_SOULDIVISION | SP_SOULEXPLOSION => {
            if !crate::npc::mapflag_vs(c, u.map) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        SP_SWHOO => {
            if !has(&sc, SC_USE_SKILL_SP_SPA) {
                return false;
            }
        }
        DK_SERVANT_W_PHANTOM | DK_SERVANT_W_DEMOL => {
            let old = if st.servantball > 0 && (st.servantball as i32) < require.spiritball {
                require.spiritball = st.servantball as i32;
                st.servantball
            } else {
                require.spiritball as i8
            };
            super::state::with(c, aid, |s| s.servantball_old = old);
        }
        IQ_SECOND_FAITH | IQ_THIRD_PUNISH => {
            if !(has(&sc, SC_FIRST_FAITH_POWER) || has(&sc, SC_SECOND_JUDGE) || has(&sc, SC_THIRD_EXOR_FLAME)) {
                return false;
            }
        }
        IQ_SECOND_JUDGEMENT | IQ_THIRD_CONSECRATION => {
            if !(has(&sc, SC_SECOND_JUDGE) || has(&sc, SC_THIRD_EXOR_FLAME)) {
                return false;
            }
        }
        IQ_SECOND_FLAME | IQ_THIRD_FLAME_BOMB => {
            if !has(&sc, SC_THIRD_EXOR_FLAME) {
                return false;
            }
        }
        SOA_SOUL_GATHERING => {
            if !has(&sc, SC_SOULCOLLECT) {
                fail(USESKILL_FAIL_CONDITION);
                return false;
            }
        }
        SOA_CIRCLE_OF_DIRECTIONS_AND_ELEMENTALS => {
            if !(has(&sc, SC_T_FOURTH_GOD) || has(&sc, SC_T_FIFTH_GOD)) {
                fail(USESKILL_FAIL_CONDITION);
                return false;
            }
        }
        SH_TEMPORARY_COMMUNION => {
            let chk = |id| crate::skill::checkskill(c, aid, id);
            if chk(SH_COMMUNE_WITH_CHUL_HO) == 0 && chk(SH_COMMUNE_WITH_HYUN_ROK) == 0 && chk(SH_COMMUNE_WITH_KI_SUL) == 0 {
                fail(USESKILL_FAIL_SH_TEMPORARY_COMMUNION);
                fail(USESKILL_FAIL);
                return false;
            }
        }
        SKE_NOON_BLAST => {
            if sc.is_none() || (!has(&sc, SC_RISING_SUN) && !has(&sc, SC_NOON_SUN) && !has(&sc, SC_SKY_ENCHANT)) {
                fail(USESKILL_FAIL_CONDITION);
                return false;
            }
        }
        SKE_SUNSET_BLAST => {
            if sc.is_none() || (!has(&sc, SC_SUNSET_SUN) && !has(&sc, SC_NOON_SUN) && !has(&sc, SC_SKY_ENCHANT)) {
                fail(USESKILL_FAIL_CONDITION);
                return false;
            }
        }
        SKE_MIDNIGHT_KICK => {
            if sc.is_none() || (!has(&sc, SC_RISING_MOON) && !has(&sc, SC_MIDNIGHT_MOON) && !has(&sc, SC_SKY_ENCHANT)) {
                fail(USESKILL_FAIL_CONDITION);
                return false;
            }
        }
        SKE_DAWN_BREAK => {
            if sc.is_none() || (!has(&sc, SC_DAWN_MOON) && !has(&sc, SC_MIDNIGHT_MOON) && !has(&sc, SC_SKY_ENCHANT)) {
                fail(USESKILL_FAIL_CONDITION);
                return false;
            }
        }
        EM_PSYCHIC_STREAM => {
            if has(&sc, SC_ENERGYCOAT) {
                fail(USESKILL_FAIL);
                return false;
            }
        }
        _ => {}
    }

    // check state required
    let u = crate::unit::get(c, aid).unwrap_or(u);
    let sc = sc_nonempty(c, &u);
    match require.state {
        ST_HIDDEN => {
            if !pc_ishiding(&u) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ST_RIDING => {
            if !pc_isriding(&u) && !pc_isridingdragon(&u) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ST_FALCON => {
            if !pc_isfalcon(&u) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ST_CART => {
            if !crate::inventory::pc_iscarton(c, aid) {
                fail(USESKILL_FAIL_CART);
                return false;
            }
        }
        ST_SHIELD => {
            if crate::pc::character_of(c, aid).map(|ch| ch.shield).unwrap_or(0) == 0 {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ST_RECOVER_WEIGHT_RATE => {
            if crate::status::data::sd_get(c, aid).map(|p| p.regen.overweight).unwrap_or(false) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ST_MOVE_ENABLE => {
            let mut u = u.clone();
            if sce(&sc, SC_COMBO).map(|e| e.val1 == skill_id as i32).unwrap_or(false) {
                // When using a combo, cancel the can't move delay to enable the skill. [Skotlex]
                u.ud.canmove_tick = c.tick();
                crate::unit::save(c, &u);
            }
            if !crate::mob::unit::unit_can_move(c, &u) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ST_WATER => {
            if has(&sc, SC_DELUGE) || has(&sc, SC_SUITON) {
                // break
            } else if crate::map_cell::getcell(c, Some(u.map), u.x, u.y, crate::map_cell::CELL_CHKWATER) != 0 && !super::iface::map_getcell_landprotector(c, u.map, u.x, u.y) {
                // break
            } else {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ST_RIDINGDRAGON => {
            if !pc_isridingdragon(&u) {
                fail(USESKILL_FAIL_DRAGON);
                return false;
            }
        }
        ST_WUG => {
            if !pc_iswug(&u) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ST_RIDINGWUG => {
            if !pc_isridingwug(&u) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ST_MADO => {
            if !pc_ismadogear(&u) {
                fail(USESKILL_FAIL_MADOGEAR);
                return false;
            }
        }
        ST_ELEMENTALSPIRIT | ST_ELEMENTALSPIRIT2 => {
            // `!sd.ed` (elemental/data.rs)
            if crate::elemental::data::of_master(c, sd.id).is_none() {
                fail(USESKILL_FAIL_EL_SUMMON);
                return false;
            }
        }
        ST_PECO => {
            if !pc_isriding(&u) {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        ST_SUNSTANCE => {
            if !(has(&sc, SC_SUNSTANCE) || has(&sc, SC_UNIVERSESTANCE)) {
                fail(USESKILL_FAIL);
                return false;
            }
        }
        ST_MOONSTANCE => {
            if !(has(&sc, SC_LUNARSTANCE) || has(&sc, SC_UNIVERSESTANCE)) {
                fail(USESKILL_FAIL);
                return false;
            }
        }
        ST_STARSTANCE => {
            if !(has(&sc, SC_STARSTANCE) || has(&sc, SC_UNIVERSESTANCE)) {
                fail(USESKILL_FAIL);
                return false;
            }
        }
        ST_UNIVERSESTANCE => {
            if !has(&sc, SC_UNIVERSESTANCE) {
                fail(USESKILL_FAIL);
                return false;
            }
        }
        _ => {}
    }

    // check the status required
    if !require.status.is_empty() {
        match skill_id {
            // Being checked later in skill_check_condition_castend()
            WZ_SIGHTRASHER => {}
            _ => {
                if !skill_check_condition_sc_required(c, sd, skill_id, &require) {
                    return false;
                }
            }
        }
    }

    // Check for equipped item(s)
    if !require.eq_item.is_empty() {
        use crate::inventory::{EQI_ACC_L, EQI_MAX};
        let mut count = require.eq_item.len();
        for &reqeqit in &require.eq_item {
            if reqeqit == 0 {
                break; // Skill has no required item(s); get out of here
            }
            match skill_id {
                // Specific skills require multiple items while default will handle singular cases
                NC_PILEBUNKER | RL_P_ALTER => {
                    if !crate::pc_equip::pc_checkequip2(c, aid, reqeqit, EQI_ACC_L, EQI_MAX) {
                        count -= 1;
                        if count == 0 {
                            if skill_id == RL_P_ALTER {
                                crate::clif_items::msg(c, sd, MSI_FAIL_NEED_EQUIPPED_PROPERTY_SAINT_BULLET);
                            } else {
                                fail(USESKILL_FAIL_THIS_WEAPON);
                            }
                            return false;
                        }
                        continue;
                    }
                }
                NC_ACCELERATION | NC_SELFDESTRUCTION | NC_SHAPESHIFT | NC_EMERGENCYCOOL | NC_MAGNETICFIELD | NC_NEUTRALBARRIER | NC_STEALTHFIELD => {
                    if pc_search_inventory(c, aid, reqeqit) == -1 {
                        count -= 1;
                        if count == 0 {
                            clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_NEED_EQUIPMENT, 0, require.eq_item[0]);
                            return false;
                        }
                        continue;
                    }
                }
                _ => {
                    if !crate::pc_equip::pc_checkequip2(c, aid, reqeqit, EQI_ACC_L, EQI_MAX) {
                        clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_NEED_EQUIPMENT, 0, reqeqit);
                        return false;
                    }
                }
            }
        }
    }

    let status = crate::status::data::sd_get(c, aid).map(|p| p.battle_status).unwrap_or_default();
    if require.mhp > 0 && get_percentage(status.hp, status.max_hp) > require.mhp as u32 {
        // mhp is the max-hp-requirement, that is, you must have this % or less of HP to cast it.
        fail(USESKILL_FAIL_HP_INSUFFICIENT);
        return false;
    }

    if require.weapon != 0 && !pc_check_weapontype(c, aid, require.weapon) {
        match skill_id {
            RA_AIMEDBOLT => {}
            _ => {
                // (uint32)log2(require.weapon): the highest weapon bit
                let w = if require.weapon > 0 { 31 - require.weapon.leading_zeros() as i32 } else { -1 };
                match w {
                    W_REVOLVER => crate::clif_items::msg(c, sd, MSI_FAIL_NEED_EQUIPPED_GUN_HANDGUN),
                    W_RIFLE => crate::clif_items::msg(c, sd, MSI_FAIL_NEED_EQUIPPED_GUN_RIFLE),
                    W_GATLING => crate::clif_items::msg(c, sd, MSI_FAIL_NEED_EQUIPPED_GUN_GATLING),
                    W_SHOTGUN => crate::clif_items::msg(c, sd, MSI_FAIL_NEED_EQUIPPED_GUN_SHOTGUN),
                    W_GRENADE => crate::clif_items::msg(c, sd, MSI_FAIL_NEED_EQUIPPED_GUN_GRANADE),
                    _ => fail(USESKILL_FAIL_THIS_WEAPON),
                }
                return false;
            }
        }
    }

    if require.sp > 0 && status.sp < require.sp as u32 {
        fail(USESKILL_FAIL_SP_INSUFFICIENT);
        return false;
    }

    if require.ap > 0 && status.ap < require.ap as u32 {
        fail(USESKILL_FAIL_AP_INSUFFICIENT);
        return false;
    }

    let zeny = crate::pc::character_of(c, aid).map(|ch| ch.zeny as i64).unwrap_or(0);
    if require.zeny > 0 && zeny < require.zeny as i64 {
        fail(USESKILL_FAIL_MONEY);
        return false;
    }

    if require.spiritball > 0 {
        // Skills that require certain types of spheres to use.
        let st = super::state::get(c, aid);
        match skill_id {
            // Skills that require soul spheres.
            SP_SOULGOLEM | SP_SOULSHADOW | SP_SOULFALCON | SP_SOULFAIRY | SP_SOULCURSE | SP_SPA | SP_SHA | SP_SWHOO | SP_SOULUNITY | SP_SOULDIVISION | SP_SOULREAPER | SP_SOULEXPLOSION | SP_KAUTE
            | SOA_EXORCISM_OF_MALICIOUS_SOUL => {
                if (st.soulball as i32) < require.spiritball {
                    fail(USESKILL_FAIL_SPIRITS);
                    return false;
                }
            }
            // Skills that requires servants.
            DK_SERVANT_W_SIGN | DK_SERVANT_W_DEMOL => {
                if (st.servantball as i32) < require.spiritball {
                    fail(USESKILL_FAIL_SPIRITS);
                    return false;
                }
            }
            DK_SERVANT_W_PHANTOM => {
                // Can be used without servantballs but if there are any they will be consumed.
            }
            _ => {
                // Skills that require spirit/coin spheres.
                if (st.spiritball as i32) < require.spiritball {
                    use crate::db::jobs::{MAPID_FIRSTMASK, MAPID_GUNSLINGER, MAPID_REBELLION, MAPID_SECONDMASK};
                    let class_ = crate::status::data::sd_get(c, aid).map(|p| p.class_).unwrap_or(0);
                    let btype = if require.spiritball == -1 { 1 } else { require.spiritball };
                    if class_ & MAPID_FIRSTMASK == MAPID_GUNSLINGER || class_ & MAPID_SECONDMASK == MAPID_REBELLION {
                        clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_COINS, btype, 0);
                    } else {
                        clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_SPIRITS, btype, 0);
                    }
                    return false;
                }
            }
        }
    }

    true
}

// ---------------------------------------------------------------------------------------------- cast end

/// skill.cpp skill_check_condition_castend: the checks when the cast ends (the ammo type and amount, the items, HP,
/// weapon, the summon limits). True when every condition passed.
pub fn skill_check_condition_castend(c: &Ctx, sd: &Unit, skill_id: u16, skill_lv: u16) -> bool {
    use crate::inventory::{data_of, inv_get, pc_search_inventory};
    let aid = sd.id;
    let mut skill_lv = skill_lv;
    if crate::pc::get(c, aid).map(|p| p.chat_id != 0).unwrap_or(false) {
        return false;
    }
    let st = super::state::get(c, aid);
    if crate::pc::has_permission(c, aid, crate::rules::PC_PERM_SKILL_UNCONDITIONAL) && st.skillitem != skill_id as i16 {
        // GMs don't override the skillItem check, otherwise they can use items without them being consumed! [Skotlex]
        unconditional_pass(c, aid, skill_id);
        return true;
    }
    // Cast start or cast end??
    if !menuskill_allows(c, aid, skill_id) {
        return false;
    }
    if st.skillitem == skill_id as i16 && !st.skillitem_keep_requirement {
        return true; // Casting finished (Item skill or Hocus-Pocus)
    }
    let u = crate::unit::get(c, aid).unwrap_or_else(|| sd.clone());
    let sc = sc_nonempty(c, &u);
    if has(&sc, SC_WEIGHT90) {
        clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_WEIGHTOVER, 0, 0);
        return false;
    }
    let land_limit = super::conf::land_skill_limit() != 0 && super::conf::land_skill_limit() & crate::map::BL_PC != 0;
    // perform skill-specific checks (and actions)
    match skill_id {
        PR_BENEDICTIO => {
            super::iface::skill_check_pc_partner(c, &u, skill_id, &mut skill_lv, 1, 1);
        }
        AM_CANNIBALIZE | AM_SPHEREMINE => {
            let mut cnt = 0;
            let summons = [MOBID_G_MANDRAGORA, MOBID_G_HYDRA, MOBID_G_FLORA, MOBID_G_PARASITE, MOBID_G_GEOGRAPHER];
            let maxcount = if skill_id == AM_CANNIBALIZE { 6 - skill_lv as i32 } else { skill_get_maxcount(skill_id, skill_lv) };
            // (summons[skill_lv-1]: a level above 5 reads past rAthena's array)
            let mob_class = if skill_id == AM_CANNIBALIZE { summons.get((skill_lv as usize).wrapping_sub(1)).copied().unwrap_or(0) } else { MOBID_MARINE_SPHERE };
            if land_limit && maxcount > 0 {
                let i = count_mob_master(c, u.map, aid, mob_class, skill_id, &mut cnt);
                if cnt >= maxcount || (skill_id == AM_CANNIBALIZE && cnt != i && crate::battle::conf::alchemist_summon_setting() & 4 != 0) {
                    // Fails when: exceed max limit. There are other plant types already out.
                    skill_fail(c, sd, skill_id);
                    return false;
                }
            }
        }
        SG_SUN_WARM | SG_MOON_WARM | SG_STAR_WARM => {
            let mut ok = false;
            if sc.is_some() {
                // Skill fails when already active
                if has(&sc, SC_WARM) {
                    super::clif::clif_skill_nodamage(c, Some(&u), &u, skill_id, skill_lv as i32, false);
                    return false;
                }
                // When having Miracle, skill succeeds regardless of map
                if has(&sc, SC_MIRACLE) {
                    ok = true;
                }
            }
            if !ok {
                if super::iface::sg_feel_map(c, aid, (skill_id - SG_SUN_WARM) as usize) != Some(u.map) {
                    super::clif::clif_skill_nodamage(c, Some(&u), &u, skill_id, skill_lv as i32, false);
                    return false;
                }
            }
        }
        NC_SILVERSNIPER | NC_MAGICDECOY => {
            let mut cnt = 0;
            let maxcount = skill_get_maxcount(skill_id, skill_lv);
            let mob_class = if skill_id == NC_MAGICDECOY { MOBID_MAGICDECOY_FIRE } else { MOBID_SILVERSNIPER };
            if land_limit && maxcount > 0 {
                if skill_id == NC_MAGICDECOY {
                    for j in mob_class..=MOBID_MAGICDECOY_WIND {
                        count_mob_master(c, u.map, aid, j, skill_id, &mut cnt);
                    }
                } else {
                    count_mob_master(c, u.map, aid, mob_class, skill_id, &mut cnt);
                }
                if cnt >= maxcount {
                    skill_fail(c, sd, skill_id);
                    return false;
                }
            }
        }
        KO_ZANZOU => {
            let mut cnt = 0;
            let i = count_mob_master(c, u.map, aid, MOBID_ZANZOU, skill_id, &mut cnt);
            if cnt >= skill_get_maxcount(skill_id, skill_lv) || cnt != i {
                skill_fail(c, sd, skill_id);
                return false;
            }
        }
        MT_SUMMON_ABR_BATTLE_WARIOR | MT_SUMMON_ABR_DUAL_CANNON | MT_SUMMON_ABR_MOTHER_NET | MT_SUMMON_ABR_INFINITY => {
            let abrs = [MOBID_ABR_BATTLE_WARIOR, MOBID_ABR_DUAL_CANNON, MOBID_ABR_MOTHER_NET, MOBID_ABR_INFINITY];
            let maxcount = skill_get_maxcount(skill_id, skill_lv);
            let mut cnt = 0;
            if land_limit && maxcount > 0 {
                let mob_class = abrs[3 - (MT_SUMMON_ABR_INFINITY - skill_id) as usize];
                count_mob_master(c, u.map, aid, mob_class, skill_id, &mut cnt);
                if cnt >= maxcount {
                    skill_fail(c, sd, skill_id);
                    return false;
                }
            }
        }
        BO_WOODENWARRIOR | BO_WOODEN_FAIRY | BO_CREEPER | BO_HELLTREE => {
            let bionics = [MOBID_BIONIC_WOODENWARRIOR, MOBID_BIONIC_WOODEN_FAIRY, MOBID_BIONIC_CREEPER, MOBID_PORING, MOBID_BIONIC_HELLTREE];
            let maxcount = skill_get_maxcount(skill_id, skill_lv);
            let mut cnt = 0;
            if land_limit && maxcount > 0 {
                let mob_class = bionics[4 - (BO_HELLTREE - skill_id) as usize];
                count_mob_master(c, u.map, aid, mob_class, skill_id, &mut cnt);
                if cnt >= maxcount {
                    skill_fail(c, sd, skill_id);
                    return false;
                }
            }
        }
        ASC_EDP => {
            // (RENEWAL)
            let item_edp = item_exists_pc(c, aid, IG_EDP);
            if item_edp < 0 {
                clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_NEED_ITEM, 1, ITEMID_POISON_BOTTLE); // [%s] required '%d' amount.
                return false;
            }
            crate::inventory::pc_delitem(c, aid, item_edp as i32, 1, 0, 1, crate::map_log::LOG_TYPE_CONSUME);
        }
        _ => {}
    }

    let u = crate::unit::get(c, aid).unwrap_or(u);
    let require = skill_get_requirement(c, &u, skill_id, skill_lv);
    let status = crate::status::data::sd_get(c, aid).map(|p| p.battle_status).unwrap_or_default();

    if require.hp > 0 && status.hp <= require.hp as u32 {
        clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_HP_INSUFFICIENT, 0, 0);
        return false;
    }

    if require.weapon != 0 && !pc_check_weapontype(c, aid, require.weapon) {
        clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_THIS_WEAPON, 0, 0);
        return false;
    }

    if require.ammo != 0 {
        // Skill requires stuff equipped in the ammo slot.
        use crate::db::items::{AMMO_BULLET, AMMO_GRENADE, AMMO_KUNAI, AMMO_SHELL};
        // 2016-10-26 kRO update made these skills require an extra ammo to cast (RENEWAL)
        let extra_ammo = match skill_id {
            WM_SEVERE_RAINSTORM | RL_FIREDANCE | RL_R_TRIP | RL_FIRE_RAIN => 1,
            _ => 0,
        };
        let idx = crate::inventory::st(c, aid).and_then(|s| s.equip_index.get(crate::inventory::EQI_AMMO).copied()).unwrap_or(-1);
        let row = if idx < 0 { None } else { inv_get(c, aid, idx as usize) };
        let Some(row) = row else {
            crate::battle::clif::clif_arrow_fail(c, sd, crate::battle::clif::ARROWFAIL_NO_AMMO);
            return false;
        };
        if (row.item.amount as i32) < require.ammo_qty + extra_ammo {
            if require.ammo & (1 << AMMO_BULLET | 1 << AMMO_GRENADE | 1 << AMMO_SHELL) != 0 {
                clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_NEED_MORE_BULLET, 0, 0);
                return false;
            } else if require.ammo & (1 << AMMO_KUNAI) != 0 {
                clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_NEED_EQUIPMENT_KUNAI, 0, 0);
                return false;
            }
            // Skill Failed. [%s] requires %dx %s.
            let e_msg = crate::data::msg_txt(381)
                .replacen("%s", skill_get_desc(skill_id), 1)
                .replacen("%d", &require.ammo_qty.to_string(), 1)
                .replacen("%s", crate::db::items::itemdb_ename(row.item.nameid), 1);
            crate::clif::messagecolor_target(c, sd, crate::social::COLOR_RED, &crate::net::wire::latin1(&e_msg), false, crate::clif::Target::SelfOnly, None);
            return false;
        }
        if require.ammo & 1i32.wrapping_shl(data_of(&row.item).subtype as u32) == 0 {
            // Ammo type check. Send the "wrong weapon type" message which is the closest we have to wrong ammo type.
            // [Skotlex] Haplo suggested we just send the equip-arrows message instead. [Skotlex]
            crate::battle::clif::clif_arrow_fail(c, sd, crate::battle::clif::ARROWFAIL_NO_AMMO);
            return false;
        }
    }

    for i in 0..MAX_SKILL_ITEM_REQUIRE {
        if require.itemid[i] == 0 {
            continue;
        }
        let index = pc_search_inventory(c, aid, require.itemid[i]);
        let short = index < 0 || inv_get(c, aid, index as usize).map(|r| (r.item.amount as i32) < require.amount[i]).unwrap_or(true);
        if short {
            match require.itemid[i] {
                ITEMID_HOLY_WATER => clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_HOLYWATER, 0, 0), // Holy water is required.
                ITEMID_RED_GEMSTONE => clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_REDJAMSTONE, 0, 0), // Red gemstone is required.
                ITEMID_BLUE_GEMSTONE => clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_BLUEJAMSTONE, 0, 0), // Blue gemstone is required.
                ITEMID_PAINT_BRUSH => clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_PAINTBRUSH, 0, 0), // Paint brush is required.
                ITEMID_ANCILLA => clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_ANCILLA, 0, 0), // Ancilla is required.
                // [%s] required '%d' amount.
                _ => clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_NEED_ITEM, require.amount[i], require.itemid[i]),
            }
            return false;
        }
    }

    // check the status required
    if !require.status.is_empty() {
        match skill_id {
            WZ_SIGHTRASHER => {
                if !skill_check_condition_sc_required(c, sd, skill_id, &require) {
                    return false;
                }
            }
            _ => {}
        }
    }

    true
}

// ---------------------------------------------------------------------------------------------- consumption

/// skill.cpp skill_consume_requirement. `type_` &1: HP / SP / AP, spheres and zeny (before the skill is used);
/// &2: items (after the skill was used).
pub fn skill_consume_requirement(c: &Ctx, sd: &Unit, skill_id: u16, skill_lv: u16, type_: i16) {
    use crate::inventory::{pc_delitem, pc_search_inventory};
    let aid = sd.id;
    let mut require = skill_get_requirement(c, sd, skill_id, skill_lv);

    if type_ & 1 != 0 {
        let st = super::state::get(c, aid);
        match skill_id {
            // TarotCard will consume sp in skill_cast_nodamage_id [Inkfish]
            CG_TAROTCARD | MC_IDENTIFY | SM_AUTOBERSERK | BD_ADAPTATION | BD_ENCORE => require.sp = 0,
            AL_HOLYLIGHT => {
                if crate::status::sc::get_sce(c, aid, SC_SPIRIT).map(|e| e.val2 == SL_PRIEST as i32).unwrap_or(false) {
                    require.sp *= 5;
                }
            }
            _ => {
                if skill_id == MO_KITRANSLATION {
                    // Spiritual Bestowment only uses spirit sphere when giving it to someone
                    require.spiritball = 0;
                    // [[fallthrough]] default
                }
                if st.autocast {
                    require.sp = 0;
                }
                // Need to remove remembered Encore skill to prevent permanent halving of SP cost
                if st.skill_id_old == BD_ENCORE && skill_id == st.skill_id_dance {
                    super::state::with(c, aid, |s| s.skill_id_old = 0);
                }
            }
        }
        if require.hp != 0 || require.sp != 0 || require.ap != 0 {
            skill_consume_hpspap(c, sd, skill_id, require.hp, require.sp, require.ap);
        }

        if require.spiritball > 0 {
            // Skills that require certain types of spheres to use
            match skill_id {
                // Skills that require soul spheres.
                SP_SOULGOLEM | SP_SOULSHADOW | SP_SOULFALCON | SP_SOULFAIRY | SP_SOULCURSE | SP_SPA | SP_SHA | SP_SWHOO | SP_SOULUNITY | SP_SOULDIVISION | SP_SOULREAPER | SP_SOULEXPLOSION | SP_KAUTE => {
                    super::state::pc_delsoulball(c, aid, require.spiritball, false);
                }
                // Skills that require servants.
                // Note: We don't update the servants display here since using these skills auto trigger an animation
                // with them in unique ways that makes them vanish.
                DK_SERVANT_W_SIGN | DK_SERVANT_W_PHANTOM | DK_SERVANT_W_DEMOL => {
                    super::state::pc_delservantball(c, aid, require.spiritball);
                }
                // Skills that require spirit/coin spheres.
                _ => super::state::pc_delspiritball(c, aid, require.spiritball, 0),
            }
        } else if require.spiritball == -1 {
            let n = super::state::with(c, aid, |s| {
                s.spiritball_old = s.spiritball;
                s.spiritball as i32
            });
            super::state::pc_delspiritball(c, aid, n, 0);
        }

        if require.zeny > 0 {
            if skill_id == NJ_ZENYNAGE {
                require.zeny = 0; // Zeny is reduced on skill_attack.
            }
            let zeny = crate::pc::character_of(c, aid).map(|ch| ch.zeny as i64).unwrap_or(0);
            if zeny < require.zeny as i64 {
                require.zeny = zeny as i32;
            }
            crate::inventory::pc_payzeny(c, aid, require.zeny as i64, crate::map_log::LOG_TYPE_CONSUME, 0);
        }
    }

    if type_ & 2 != 0 {
        if skill_id == MC_IDENTIFY && (require.hp != 0 || require.sp != 0 || require.ap != 0) {
            skill_consume_hpspap(c, sd, skill_id, require.hp, require.sp, require.ap);
        }
        let sc = crate::unit::get(c, aid).and_then(|u| sc_nonempty(c, &u));
        for i in 0..MAX_SKILL_ITEM_REQUIRE {
            if require.itemid[i] == 0 {
                continue;
            }
            if crate::db::items::group::itemdb_group_item_exists(IG_GEMSTONE, require.itemid[i]) && skill_id != HW_GANBANTEIN && sce(&sc, SC_SPIRIT).map(|e| e.val2 == SL_WIZARD as i32).unwrap_or(false) {
                continue; // Gemstones are checked, but not substracted from inventory.
            }
            let keep = match skill_id {
                SA_SEISMICWEAPON => has(&sc, SC_UPHEAVAL_OPTION) && c.rnd() % 100 < 50,
                SA_FLAMELAUNCHER | SA_VOLCANO => has(&sc, SC_TROPIC_OPTION) && c.rnd() % 100 < 50,
                SA_FROSTWEAPON | SA_DELUGE => has(&sc, SC_CHILLY_AIR_OPTION) && c.rnd() % 100 < 50,
                SA_LIGHTNINGLOADER | SA_VIOLENTGALE => has(&sc, SC_WILD_STORM_OPTION) && c.rnd() % 100 < 50,
                _ => false,
            };
            if keep {
                continue;
            }
            let n = pc_search_inventory(c, aid, require.itemid[i]);
            if n >= 0 {
                pc_delitem(c, aid, n as i32, require.amount[i], 0, 1, crate::map_log::LOG_TYPE_CONSUME);
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------- requirement

/// skill.cpp skill_get_requirement: the skill level's requirements after item bonuses, statuses and the skill's own
/// reductions (all zero for item skills / Hocus-Pocus and while a toggle skill is being turned off).
pub fn skill_get_requirement(c: &Ctx, sd: &Unit, skill_id: u16, skill_lv: u16) -> SkillCondition {
    use crate::db::items::group::itemdb_group_item_exists;
    let mut req = SkillCondition::default();
    // `if (!sd) return req`
    if sd.kind != crate::map::BL_PC {
        return req;
    }
    let aid = sd.id;
    let Some(pst) = crate::status::data::sd_get(c, aid) else { return req };
    let st = super::state::get(c, aid);
    if st.skillitem == skill_id as i16 && !st.skillitem_keep_requirement {
        return req; // Item skills and Hocus-Pocus don't have requirements.[Inkfish]
    }
    let sc = sc_nonempty(c, sd);
    // Checks if disabling skill - in which case no SP requirements are necessary
    if sc.is_some() && super::misc::skill_disable_check(c, aid, skill_id) {
        return req;
    }
    let mut skill_lv = skill_lv.clamp(1, MAX_SKILL_LEVEL as u16);
    let status = &pst.battle_status;
    // (rAthena dereferences skill_db.find's result: callers only pass skills of the database)
    let Some(skill) = skill_db().find(skill_id) else { return req };
    let skills = crate::skilltree::Skills::load(c, aid);
    let l = skill_lv as usize - 1;

    // (uint32 status values times the int32 rate: C's unsigned arithmetic, then added to the int32 cost)
    req.hp = skill.require.hp[l];
    let hp_rate = skill.require.hp_rate[l];
    let add = if hp_rate > 0 { status.hp.wrapping_mul(hp_rate as u32) / 100 } else { status.max_hp.wrapping_mul(hp_rate.wrapping_neg() as u32) / 100 };
    req.hp = req.hp.wrapping_add(add as i32);

    req.sp = skill.require.sp[l];
    if st.skill_id_old == BD_ENCORE && skill_id == st.skill_id_dance {
        req.sp /= 2;
    }
    if st.skill_id_old == TR_RETROSPECTION && skill_id == st.skill_id_song {
        req.sp -= req.sp * 30 / 100;
    }
    let sp_rate = skill.require.sp_rate[l];
    let add = if sp_rate > 0 { status.sp.wrapping_mul(sp_rate as u32) / 100 } else { status.max_sp.wrapping_mul(sp_rate.wrapping_neg() as u32) / 100 };
    req.sp = req.sp.wrapping_add(add as i32);
    if pst.rates.dsprate != 100 {
        req.sp = req.sp.wrapping_mul(pst.rates.dsprate) / 100;
    }
    let mut sp_skill_rate_bonus: i32 = 100;
    if let Some(it) = pst.lists.skillusesprate.iter().find(|it| it.id == skill_id) {
        sp_skill_rate_bonus -= it.val;
    }
    if let Some(it) = pst.lists.skillusesp.iter().find(|it| it.id == skill_id) {
        req.sp -= it.val;
    }
    // sd->status.skill[sd->reproduceskill_idx].id
    if skills.as_ref().map(|s| s.at(s.meta.reproduceskill_idx).id == skill_id).unwrap_or(false) {
        req.sp += req.sp * 30 / 100;
    }
    req.sp = (req.sp.wrapping_mul(sp_skill_rate_bonus) / 100).clamp(0, i16::MAX as i32);

    if let Some(s) = &sc {
        if s.has(SC__LAZINESS) {
            req.sp += req.sp + s.v1(SC__LAZINESS) * 10;
        }
        if s.has(SC_RECOGNIZEDSPELL) {
            req.sp += req.sp / 4;
        }
        if s.has(SC_OFFERTORIUM) {
            req.sp += req.sp * s.v3(SC_OFFERTORIUM) / 100;
        }
        if s.has(SC_TELEKINESIS_INTENSE) && skill_get_ele(skill_id, skill_lv) == ELE_GHOST {
            req.sp -= req.sp * s.v2(SC_TELEKINESIS_INTENSE) / 100;
        }
        // (RENEWAL)
        if s.has(SC_ADAPTATION) && skill_get_inf2(skill_id, &[INF2_ISSONG]) {
            req.sp -= req.sp * 20 / 100;
        }
        if s.has(SC_NIBELUNGEN) && s.v2(SC_NIBELUNGEN) == RINGNBL_SPCONSUM {
            req.sp -= req.sp * 30 / 100;
        }
        if s.has(SC_GLOOMYDAY) {
            req.sp += req.sp * (skill_lv as i32 * 10) / 100;
        }
        if s.has(SC_CRESCIVEBOLT) {
            req.sp += req.sp * (20 * s.v1(SC_CRESCIVEBOLT)) / 100;
        }
    }

    req.ap = skill.require.ap[l];
    let ap_rate = skill.require.ap_rate[l];
    let add = if ap_rate > 0 { status.ap.wrapping_mul(ap_rate as u32) / 100 } else { status.max_ap.wrapping_mul(ap_rate.wrapping_neg() as u32) / 100 };
    req.ap = req.ap.wrapping_add(add as i32);

    req.zeny = skill.require.zeny[l];

    if let Some(e) = sce(&sc, SC__UNLUCKY) {
        if e.val1 < 3 {
            req.zeny += e.val1 * 250;
        } else {
            req.zeny += 1000;
        }
    }

    req.spiritball = skill.require.spiritball[l];
    req.state = skill.require.state;

    req.mhp = skill.require.mhp[l];
    req.weapon = skill.require.weapon;
    req.ammo_qty = skill.require.ammo_qty[l];
    let weapontype1 = crate::inventory::st(c, aid).map(|s| s.weapontype1 as i32).unwrap_or(W_FIST);
    if skill_id == NW_MAGAZINE_FOR_ONE && weapontype1 == W_GATLING {
        req.ammo_qty += 4;
    }
    if req.ammo_qty != 0 {
        req.ammo = skill.require.ammo;
    }

    if req.ammo == 0 && skill_id != 0 && skill_isammotype(c, aid, skill_id) {
        // Assume this skill is using the weapon, therefore it requires arrows.
        req.ammo = AMMO_TYPE_ALL; // Enable use on all ammo types.
        req.ammo_qty = 1;
    }

    req.status = skill.require.status.iter().map(|&s| s as i32).collect();
    req.eq_item = skill.require.eq_item.clone();

    // Level dependence flag is determined based on the ItemCost Level label
    let level_dependent = skill.require.itemid_level_dependent;
    let no_gemstone = pst.special_state.no_gemstone;

    // switch (skill_id): Skill level-dependent checks
    if matches!(skill_id, NC_SHAPESHIFT | NC_REPAIR) {
        // NOTE: Magic_Gear_Fuel / Repair_Kit must be last in the ItemCost list depending on the skill's max level
        // (itemid[skill->max]: a max level of 10 or more reads past rAthena's array)
        let m = skill.max as usize;
        req.itemid[1] = skill.require.itemid.get(m).copied().unwrap_or(0);
        req.amount[1] = skill.require.amount.get(m).copied().unwrap_or(0);
        // [[fallthrough]] default
    }
    // Normal skill requirements and gemstone checks
    let n = if !level_dependent { MAX_SKILL_ITEM_REQUIRE } else { 2 };
    let mut i: usize = 0;
    while i < n {
        // Skip this for level_dependent requirement, just looking forward for gemstone removal. Assumed if there is
        // gemstone there.
        if !level_dependent {
            let mut skip = false;
            match skill_id {
                AM_POTIONPITCHER | CR_SLIMPITCHER | CR_CULTIVATION => {
                    if i as i32 != skill_lv as i32 % 11 - 1 {
                        skip = true;
                    }
                }
                AM_CALLHOMUN => {
                    // (RENEWAL, skill.cpp:9883-9899) sd->hd: the player's loaded homunculus (homunculus/data.rs)
                    let hd = crate::homunculus::data::of_master(c, aid);
                    // Player has no homunculus, only requires first item
                    if i > 0 && hd.is_none() {
                        i = MAX_SKILL_ITEM_REQUIRE;
                        skip = true;
                    } else if super::area::temp(0) == 1 || hd.as_ref().map(|hd| hd.homunculus.vaporize as i32 == crate::homunculus::data::HOM_ST_REST).unwrap_or(false) {
                        // Recalling from Rest state has a different consume item (stored as second item)
                        req.itemid[0] = skill.require.itemid[1];
                        req.amount[0] = skill.require.amount[1];
                        if super::area::temp(0) == 1 && crate::homunculus::data::hom_is_active(hd.as_ref()) {
                            super::area::set_temp(0, 0);
                        }
                        i = MAX_SKILL_ITEM_REQUIRE;
                        skip = true;
                    }
                }
                AB_ADORAMUS => {
                    if itemdb_group_item_exists(IG_GEMSTONE, skill.require.itemid[i]) && (no_gemstone == 2 || super::iface::skill_check_pc_partner(c, sd, skill_id, &mut skill_lv, 1, 2) != 0) {
                        skip = true;
                    }
                }
                _ => {}
            }
            if skip {
                i += 1;
                continue;
            }

            req.itemid[i] = skill.require.itemid[i];
            req.amount[i] = skill.require.amount[i];

            if ((HT_SKIDTRAP..=HT_TALKIEBOX).contains(&skill_id) && crate::skill::checkskill(c, aid, RA_RESEARCHTRAP) > 0) || skill_id == SC_ESCAPE {
                let it_index = crate::inventory::pc_search_inventory(c, aid, req.itemid[i]);
                let short = it_index < 0 || crate::inventory::inv_get(c, aid, it_index as usize).map(|r| (r.item.amount as i32) < req.amount[i]).unwrap_or(true);
                if short {
                    if skill_id == SC_ESCAPE {
                        // Alloy Trap has priority over normal Trap
                        req.itemid[i] = ITEMID_TRAP;
                    } else {
                        req.itemid[i] = ITEMID_TRAP_ALLOY;
                    }
                    req.amount[i] = 1;
                }
                break; // (breaks the item loop, as rAthena's `break` there does)
            }
        } else {
            // Process level_dependent requirement
            if level_dependent && skill_lv as usize <= MAX_SKILL_ITEM_REQUIRE && i == 0 {
                req.itemid[0] = skill.require.itemid[skill_lv as usize - 1];
                req.amount[0] = skill.require.amount[skill_lv as usize - 1];
            }
        }

        // Check requirement for gemstone.
        if itemdb_group_item_exists(IG_GEMSTONE, req.itemid[i]) {
            if no_gemstone == 2 {
                // Remove all Magic Stone required for all skills for VIP.
                req.itemid[i] = 0;
                req.amount[i] = 0;
            } else if no_gemstone != 0 || has(&sc, SC_INTOABYSS) {
                // All gem skills except Hocus Pocus and Ganbantein can cast for free with Mistress card -helvetica
                if skill_id != SA_ABRACADABRA && skill_id != HW_GANBANTEIN {
                    req.itemid[i] = 0;
                    req.amount[i] = 0;
                } else {
                    req.amount[i] -= 1;
                    if req.amount[i] < 1 {
                        req.amount[i] = 1; // Hocus Pocus always use at least 1 gem
                    }
                }
            }
        }
        // Check requirement for Magic Gear Fuel
        if req.itemid[i] == ITEMID_MAGIC_GEAR_FUEL && pst.special_state.no_mado_fuel {
            req.itemid[i] = 0;
            req.amount[i] = 0;
        }
        i += 1;
    }

    // Check for cost reductions due to skills & SCs
    match skill_id {
        MC_MAMMONITE | WS_CARTTERMINATION => {
            // (RENEWAL: WS_CARTTERMINATION too, 20%; pre-renewal 10%)
            if crate::skill::checkskill(c, aid, BS_UNFAIRLYTRICK) > 0 {
                req.zeny -= req.zeny * 20 / 100;
            }
        }
        SL_SMA | SL_STUN | SL_STIN => {
            let kaina_lv = crate::skill::checkskill(c, aid, SL_KAINA) as i32;
            let base_level = crate::pc::character_of(c, aid).map(|ch| ch.base_level as i32).unwrap_or(0);
            if !(kaina_lv == 0 || base_level < 70) {
                if base_level >= 90 {
                    req.sp -= req.sp * 7 * kaina_lv / 100;
                } else if base_level >= 80 {
                    req.sp -= req.sp * 5 * kaina_lv / 100;
                } else if base_level >= 70 {
                    req.sp -= req.sp * 3 * kaina_lv / 100;
                }
            }
        }
        MO_CHAINCOMBO | MO_COMBOFINISH | CH_TIGERFIST | CH_CHAINCRUSH => {
            if sce(&sc, SC_SPIRIT).map(|e| e.val2 == SL_MONK as i32).unwrap_or(false) {
                req.sp = 2; // Monk Spirit makes monk/champion combo skills cost 2 SP regardless of original cost
            }
        }
        MO_BODYRELOCATION => {
            if has(&sc, SC_EXPLOSIONSPIRITS) {
                req.spiritball = 0;
            }
        }
        MO_EXTREMITYFIST => {
            if sc.is_some() {
                if has(&sc, SC_BLADESTOP) {
                    req.spiritball -= 1;
                } else if has(&sc, SC_COMBO) {
                    // (RENEWAL; pre-renewal: 4 after Combo Finish, the rest after Chain Crush)
                    req.spiritball = if st.spiritball != 0 { st.spiritball as i32 } else { 1 };
                } else if has(&sc, SC_RAISINGDRAGON) && st.spiritball > 5 {
                    req.spiritball = st.spiritball as i32; // must consume all regardless of the amount required
                }
            }
        }
        LG_RAGEBURST => {
            req.spiritball = if st.spiritball != 0 { st.spiritball as i32 } else { 1 };
        }
        SR_FALLENEMPIRE => {
            if has(&sc, SC_FIRST_FAITH_POWER) || has(&sc, SC_SECOND_JUDGE) || has(&sc, SC_THIRD_EXOR_FLAME) {
                req.spiritball = 0;
            }
        }
        SR_TIGERCANNON => {
            if has(&sc, SC_THIRD_EXOR_FLAME) {
                req.spiritball = 0;
            }
        }
        SR_RAMPAGEBLASTER | SR_RIDEINLIGHTNING => {
            if has(&sc, SC_MASSIVE_F_BLASTER) {
                req.spiritball = 0;
            }
        }
        SR_GATEOFHELL => {
            if sce(&sc, SC_COMBO).map(|e| e.val1 == SR_FALLENEMPIRE as i32).unwrap_or(false) {
                req.sp -= req.sp * 10 / 100;
            }
        }
        SR_FLASHCOMBO => {
            if has(&sc, SC_SECOND_JUDGE) || has(&sc, SC_THIRD_EXOR_FLAME) {
                req.spiritball = 0;
            }
        }
        SO_SUMMON_AGNI | SO_SUMMON_AQUA | SO_SUMMON_VENTUS | SO_SUMMON_TERA => {
            let spirit_sympathy = crate::skill::checkskill(c, aid, SO_EL_SYMPATHY) as i32;
            if spirit_sympathy != 0 {
                req.sp -= req.sp * (5 + 5 * spirit_sympathy) / 100;
            }
        }
        NPC_PSYCHIC_WAVE | SO_PSYCHIC_WAVE => {
            if has(&sc, SC_HEATER_OPTION) || has(&sc, SC_COOLER_OPTION) || has(&sc, SC_CURSED_SOIL_OPTION) || has(&sc, SC_BLAST_OPTION) {
                req.sp += req.sp / 2; // 1.5x SP cost
            }
        }
        BO_ACIDIFIED_ZONE_WATER | BO_ACIDIFIED_ZONE_GROUND | BO_ACIDIFIED_ZONE_WIND | BO_ACIDIFIED_ZONE_FIRE => {
            if has(&sc, SC_RESEARCHREPORT) && req.amount[0] > 0 {
                req.amount[0] -= 1;
            }
        }
        MT_A_MACHINE => {
            if has(&sc, SC_ABR_INFINITY) {
                req.amount[0] = 0;
            }
        }
        _ => {}
    }

    // Check if player is using the copied skill [Cydh]
    let idx = skill_get_index(skill_id);
    if skills.as_ref().map(|s| s.at(idx).flag == SKILL_FLAG_PLAGIARIZED).unwrap_or(false) {
        let req_opt = skill.copyable.req_opt;
        if req_opt & SKILL_REQ_HPCOST != 0 {
            req.hp = 0;
        }
        if req_opt & SKILL_REQ_MAXHPTRIGGER != 0 {
            req.mhp = 0;
        }
        if req_opt & SKILL_REQ_SPCOST != 0 {
            req.sp = 0;
        }
        if req_opt & SKILL_REQ_HPRATECOST != 0 {
            req.hp_rate = 0;
        }
        if req_opt & SKILL_REQ_SPRATECOST != 0 {
            req.sp_rate = 0;
        }
        if req_opt & SKILL_REQ_ZENYCOST != 0 {
            req.zeny = 0;
        }
        if req_opt & SKILL_REQ_WEAPON != 0 {
            req.weapon = 0;
        }
        if req_opt & SKILL_REQ_AMMO != 0 {
            req.ammo = 0;
            req.ammo_qty = 0;
        }
        if req_opt & SKILL_REQ_STATE != 0 {
            req.state = ST_NONE;
        }
        if req_opt & SKILL_REQ_STATUS != 0 {
            req.status.clear();
            req.status.shrink_to_fit();
        }
        if req_opt & SKILL_REQ_SPIRITSPHERECOST != 0 {
            req.spiritball = 0;
        }
        if req_opt & SKILL_REQ_ITEMCOST != 0 {
            req.itemid = [0; MAX_SKILL_ITEM_REQUIRE];
            req.amount = [0; MAX_SKILL_ITEM_REQUIRE];
        }
        if req_opt & SKILL_REQ_EQUIPMENT != 0 {
            req.eq_item.clear();
            req.eq_item.shrink_to_fit();
        }
        if req_opt & SKILL_REQ_APCOST != 0 {
            req.ap = 0;
        }
        if req_opt & SKILL_REQ_APRATECOST != 0 {
            req.ap_rate = 0;
        }
    }

    req
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The map flag, item group, ammo and element values named here equal rAthena's build (script constants).
    #[test]
    fn constants_match_script_constants() {
        let k = |n: &str| crate::data::constant(n).unwrap_or_else(|| panic!("constant {n}"));
        assert_eq!(k("mf_nobuyingstore"), MF_NOBUYINGSTORE as i64);
        assert_eq!(k("mf_noicewall"), MF_NOICEWALL_ as i64);
        assert_eq!(k("mf_novending"), crate::inventory::MF_NOVENDING as i64);
        assert_eq!(k("IG_GEMSTONE"), IG_GEMSTONE as i64);
        assert_eq!(k("IG_EDP"), IG_EDP as i64);
        assert_eq!(k("MAX_AMMO_TYPE"), MAX_AMMO_TYPE as i64);
        assert_eq!(k("ELE_GHOST"), ELE_GHOST as i64);
        assert_eq!(k("W_BOW"), W_BOW as i64);
        assert_eq!(k("W_REVOLVER"), W_REVOLVER as i64);
        assert_eq!(k("W_GRENADE"), W_GRENADE as i64);
        assert_eq!(k("AMMO_BULLET"), crate::db::items::AMMO_BULLET as i64);
        assert_eq!(k("AMMO_SHELL"), crate::db::items::AMMO_SHELL as i64);
        assert_eq!(k("AMMO_GRENADE"), crate::db::items::AMMO_GRENADE as i64);
        assert_eq!(k("AMMO_KUNAI"), crate::db::items::AMMO_KUNAI as i64);
        assert_eq!(AMMO_TYPE_ALL, 0x3FF);
    }

    /// itemdb.hpp's item ids name the items rAthena's item database gives them (AegisName).
    #[test]
    fn item_ids_match_the_item_database() {
        let name = |id: u32| crate::db::items::itemdb_exists(id).map(|d| d.name.clone()).unwrap_or_else(|| panic!("item {id}"));
        assert_eq!(name(ITEMID_HOLY_WATER), "Holy_Water");
        assert_eq!(name(ITEMID_POISON_BOTTLE), "Poison_Bottle");
        assert_eq!(name(ITEMID_EMPTY_BOTTLE), "Empty_Bottle");
        assert_eq!(name(ITEMID_RED_GEMSTONE), "Red_Gemstone");
        assert_eq!(name(ITEMID_BLUE_GEMSTONE), "Blue_Gemstone");
        assert_eq!(name(ITEMID_TRAP), "Booby_Trap");
        assert_eq!(name(ITEMID_PAINT_BRUSH), "Paint_Brush");
        assert_eq!(name(ITEMID_MAGIC_GEAR_FUEL), "Magic_Gear_Fuel");
        assert_eq!(name(ITEMID_TRAP_ALLOY), "Special_Alloy_Trap");
        assert_eq!(name(ITEMID_ANCILLA), "Ansila");
        // the gemstones are in IG_GEMSTONE, the Poison Bottle in IG_EDP
        assert!(crate::db::items::group::itemdb_group_item_exists(IG_GEMSTONE, ITEMID_BLUE_GEMSTONE));
        assert!(crate::db::items::group::itemdb_group_item_exists(IG_EDP, ITEMID_POISON_BOTTLE));
    }

    /// mob.hpp's MOBID values name the monsters rAthena's monster database gives them (AegisName).
    #[test]
    fn mob_ids_match_the_monster_database() {
        let name = |id: i32| crate::db::mobs::mob_db().find(id as u32).map(|m| m.sprite.clone()).unwrap_or_else(|| panic!("mob {id}"));
        assert_eq!(name(MOBID_MARINE_SPHERE), "MARINE_SPHERE");
        assert_eq!(name(MOBID_G_PARASITE), "G_PARASITE");
        assert_eq!(name(MOBID_G_FLORA), "G_FLORA");
        assert_eq!(name(MOBID_G_HYDRA), "G_HYDRA");
        assert_eq!(name(MOBID_G_MANDRAGORA), "G_MANDRAGORA");
        assert_eq!(name(MOBID_G_GEOGRAPHER), "G_GEOGRAPHER");
        assert_eq!(name(MOBID_GUARDIAN_STONE1), "S_EMPEL_1");
        assert_eq!(name(MOBID_GUARDIAN_STONE2), "S_EMPEL_2");
        assert_eq!(name(MOBID_SILVERSNIPER), "SILVERSNIPER");
        assert_eq!(name(MOBID_MAGICDECOY_FIRE), "MAGICDECOY_FIRE");
        assert_eq!(name(MOBID_MAGICDECOY_WIND), "MAGICDECOY_WIND");
        assert_eq!(name(MOBID_ZANZOU), "KO_KAGE");
        assert_eq!(name(MOBID_ABR_BATTLE_WARIOR), "ABR_BATTLE_WARIOR");
        assert_eq!(name(MOBID_ABR_DUAL_CANNON), "ABR_DUAL_CANNON");
        assert_eq!(name(MOBID_ABR_MOTHER_NET), "ABR_MOTHER_NET");
        assert_eq!(name(MOBID_ABR_INFINITY), "ABR_INFINITY");
        assert_eq!(name(MOBID_BIONIC_WOODENWARRIOR), "SUMMON_WOODENWARRIOR");
        assert_eq!(name(MOBID_BIONIC_WOODEN_FAIRY), "SUMMON_WOODEN_FAIRY");
        assert_eq!(name(MOBID_BIONIC_CREEPER), "SUMMON_CREEPER");
        assert_eq!(name(MOBID_BIONIC_HELLTREE), "SUMMON_HELLTREE");
    }

    /// The skill-id arithmetic of the case arms (skill_id - SG_SUN_COMFORT, abrs[3 - (INFINITY - id)],
    /// bionics[4 - (HELLTREE - id)]) relies on these ids being consecutive.
    #[test]
    fn skill_id_ranges_are_consecutive() {
        assert_eq!((SG_MOON_COMFORT - SG_SUN_COMFORT, SG_STAR_COMFORT - SG_SUN_COMFORT), (1, 2));
        assert_eq!((SG_MOON_WARM - SG_SUN_WARM, SG_STAR_WARM - SG_SUN_WARM), (1, 2));
        assert_eq!(MT_SUMMON_ABR_INFINITY - MT_SUMMON_ABR_BATTLE_WARIOR, 3);
        assert_eq!(BO_HELLTREE - BO_WOODENWARRIOR, 4);
        assert_eq!(SC_SPHERE_5 - SC_SPHERE_1, 4);
    }

    /// common/utils.cpp get_percentage floors the double: 29 of 100 is 28 (100 * 0.29 = 28.999…).
    #[test]
    fn get_percentage_is_the_double_floor() {
        assert_eq!(get_percentage(29, 100), 28);
        assert_eq!(get_percentage(50, 100), 50);
        assert_eq!(get_percentage(1, 0), u32::MAX);
    }
}
