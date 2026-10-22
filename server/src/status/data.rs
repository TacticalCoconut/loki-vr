//! The status system's state as tables. rAthena keeps it in map_session_data (base_status / battle_status,
//! weapon_data, indexed_bonus, the bonus vectors, regen data, rates, special_state …) and in status_change; here it is
//! one `pc_status` row per online character (what status_calc_pc fills), a small hot `pc_regen_tick` row (regen tick
//! accumulators that natural heal moves every NATURAL_HEAL_INTERVAL), `sc_entry` rows (status_change entries of any
//! unit), `sc_unit` (the rest of class status_change), `sc_saved` (char-server `sc_data`), `bonus_script` (sd->bonus_script
//! list / char-server `bonus_script`) and the `status_world` singleton (status.cpp's globals).
//!
//! While status_calc_pc runs, scripts (item bonuses) change the character's row many times; the row is then held in
//! memory ([`with_sd`] / [`sd_get`] / [`sd_put`] see it) and written once when the outermost calculation ends — the same
//! object rAthena's run_script calls work on.

use std::cell::RefCell;

use spacetimedb::{table, SpacetimeType, Table};

use super::defs::*;
use crate::world::{Ctx, Tick};

/// status.hpp struct weapon_atk (RENEWAL: matk and wlv).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct WeaponAtk {
    pub atk: u16,
    pub atk2: u16,
    pub range: u16,
    pub ele: u8,
    pub matk: u16,
    pub wlv: u8,
}

/// status.hpp struct status_data (RENEWAL, RENEWAL_ASPD; defType = int16).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct StatusData {
    pub hp: u32,
    pub sp: u32,
    pub ap: u32,
    pub max_hp: u32,
    pub max_sp: u32,
    pub max_ap: u32,
    pub str: i16,
    pub agi: i16,
    pub vit: i16,
    pub int_: i16,
    pub dex: i16,
    pub luk: i16,
    pub pow: i16,
    pub sta: i16,
    pub wis: i16,
    pub spl: i16,
    pub con: i16,
    pub crt: i16,
    pub eatk: i16,
    pub batk: i32,
    pub watk: u16,
    pub watk2: u16,
    pub matk_min: u16,
    pub matk_max: u16,
    pub speed: u16,
    pub amotion: u16,
    pub clientamotion: u16,
    pub adelay: u16,
    pub dmotion: u16,
    pub mode: i32,
    pub hit: i16,
    pub flee: i16,
    pub cri: i16,
    pub flee2: i16,
    pub def2: i16,
    pub mdef2: i16,
    pub aspd_rate2: i16,
    pub aspd_rate: i16,
    pub patk: i16,
    pub smatk: i16,
    pub res: i16,
    pub mres: i16,
    pub hplus: i16,
    pub crate_: i16,
    pub def: i16,
    pub mdef: i16,
    pub def_ele: u8,
    pub ele_lv: u8,
    pub size: u8,
    pub race: u8,
    pub class_: u8,
    pub rhw: WeaponAtk,
    pub lhw: WeaponAtk,
}

impl StatusData {
    /// status.cpp status_cpy: everything but hp / sp / ap.
    pub fn cpy_from(&mut self, b: &StatusData) {
        let (hp, sp, ap) = (self.hp, self.sp, self.ap);
        *self = b.clone();
        self.hp = hp;
        self.sp = sp;
        self.ap = ap;
    }
    /// status_calc_pc_sub's memset(&base_status->max_hp, 0, …): everything but hp / sp / ap is zeroed.
    pub fn zero_keep_hpspap(&mut self) {
        let (hp, sp, ap) = (self.hp, self.sp, self.ap);
        *self = StatusData { hp, sp, ap, ..Default::default() };
    }
}

/// pc.hpp s_item_bonus.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct ItemBonus {
    pub id: u16,
    pub val: i32,
}

/// pc.hpp s_addele2.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct AddEle2 {
    pub flag: i16,
    pub rate: i16,
    pub ele: u8,
}

/// pc.hpp s_addrace2.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct AddRace2 {
    pub flag: i16,
    pub rate: i16,
    pub race: u8,
}

/// pc.hpp weapon_data::drain_data.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct DrainData {
    pub rate: i16,
    pub per: i16,
}

/// pc.hpp struct weapon_data.
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct WeaponData {
    /// atkmods[SZ_ALL] (not zeroed by status_calc_pc).
    pub atkmods: Vec<i32>,
    pub overrefine: i32,
    pub star: i32,
    pub ignore_def_ele: i32,
    pub ignore_def_race: i32,
    pub ignore_def_class: i32,
    pub def_ratio_atk_ele: i32,
    pub def_ratio_atk_race: i32,
    pub def_ratio_atk_class: i32,
    pub addele: Vec<i32>,
    pub addrace: Vec<i32>,
    pub addclass: Vec<i32>,
    pub addrace2: Vec<i32>,
    pub addsize: Vec<i32>,
    pub hp_drain_race: Vec<i16>,
    pub sp_drain_race: Vec<i16>,
    pub hp_drain_class: Vec<i16>,
    pub sp_drain_class: Vec<i16>,
    pub hp_drain_rate: DrainData,
    pub sp_drain_rate: DrainData,
    pub add_dmg: Vec<ItemBonus>,
    pub addele2: Vec<AddEle2>,
    pub addrace3: Vec<AddRace2>,
}

impl Default for WeaponData {
    fn default() -> Self {
        WeaponData {
            atkmods: vec![0; SZ_ALL as usize],
            overrefine: 0,
            star: 0,
            ignore_def_ele: 0,
            ignore_def_race: 0,
            ignore_def_class: 0,
            def_ratio_atk_ele: 0,
            def_ratio_atk_race: 0,
            def_ratio_atk_class: 0,
            addele: vec![0; ELE_MAX],
            addrace: vec![0; RC_MAX],
            addclass: vec![0; CLASS_MAX],
            addrace2: vec![0; RC2_MAX],
            addsize: vec![0; SZ_MAX],
            hp_drain_race: vec![0; RC_MAX],
            sp_drain_race: vec![0; RC_MAX],
            hp_drain_class: vec![0; CLASS_MAX],
            sp_drain_class: vec![0; CLASS_MAX],
            hp_drain_rate: DrainData::default(),
            sp_drain_rate: DrainData::default(),
            add_dmg: Vec::new(),
            addele2: Vec::new(),
            addrace3: Vec::new(),
        }
    }
}

impl WeaponData {
    /// status_calc_pc_sub: `memset(&wd.overrefine, 0, sizeof(wd) - sizeof(wd.atkmods))` — every field but atkmods.
    /// (The memset also clears the vectors' storage in rAthena, which leaves them empty.)
    pub fn reset_keep_atkmods(&mut self) {
        let atkmods = std::mem::take(&mut self.atkmods);
        *self = WeaponData { atkmods, ..Default::default() };
    }
}

/// map_session_data::s_indexed_bonus (zeroed at the start of every status_calc_pc).
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct IndexedBonus {
    pub param_bonus: Vec<i32>,
    pub param_equip: Vec<i32>,
    pub subele: Vec<i32>,
    pub subele_script: Vec<i32>,
    pub subdefele: Vec<i32>,
    pub subrace: Vec<i32>,
    pub subclass: Vec<i32>,
    pub subrace2: Vec<i32>,
    pub subsize: Vec<i32>,
    pub coma_class: Vec<i16>,
    pub coma_race: Vec<i16>,
    pub weapon_coma_ele: Vec<i16>,
    pub weapon_coma_race: Vec<i16>,
    pub weapon_coma_class: Vec<i16>,
    pub weapon_atk: Vec<i32>,
    pub weapon_damage_rate: Vec<i32>,
    pub arrow_addele: Vec<i32>,
    pub arrow_addrace: Vec<i32>,
    pub arrow_addclass: Vec<i32>,
    pub arrow_addsize: Vec<i32>,
    pub magic_addele: Vec<i32>,
    pub magic_addele_script: Vec<i32>,
    pub magic_addrace: Vec<i32>,
    pub magic_addclass: Vec<i32>,
    pub magic_addsize: Vec<i32>,
    pub magic_atk_ele: Vec<i32>,
    pub weapon_subsize: Vec<i32>,
    pub magic_subsize: Vec<i32>,
    pub critaddrace: Vec<i32>,
    pub expaddrace: Vec<i32>,
    pub expaddclass: Vec<i32>,
    pub ignore_mdef_by_race: Vec<i32>,
    pub ignore_mdef_by_class: Vec<i32>,
    pub ignore_def_by_race: Vec<i32>,
    pub ignore_def_by_class: Vec<i32>,
    pub sp_gain_race: Vec<i16>,
    pub magic_addrace2: Vec<i32>,
    pub ignore_mdef_by_race2: Vec<i32>,
    pub dropaddrace: Vec<i32>,
    pub dropaddclass: Vec<i32>,
    pub magic_subdefele: Vec<i32>,
    pub ignore_res_by_race: Vec<i32>,
    pub ignore_mres_by_race: Vec<i32>,
}

impl Default for IndexedBonus {
    fn default() -> Self {
        IndexedBonus {
            param_bonus: vec![0; PARAM_MAX],
            param_equip: vec![0; PARAM_MAX],
            subele: vec![0; ELE_MAX],
            subele_script: vec![0; ELE_MAX],
            subdefele: vec![0; ELE_MAX],
            subrace: vec![0; RC_MAX],
            subclass: vec![0; CLASS_MAX],
            subrace2: vec![0; RC2_MAX],
            subsize: vec![0; SZ_MAX],
            coma_class: vec![0; CLASS_MAX],
            coma_race: vec![0; RC_MAX],
            weapon_coma_ele: vec![0; ELE_MAX],
            weapon_coma_race: vec![0; RC_MAX],
            weapon_coma_class: vec![0; CLASS_MAX],
            weapon_atk: vec![0; 16],
            weapon_damage_rate: vec![0; 16],
            arrow_addele: vec![0; ELE_MAX],
            arrow_addrace: vec![0; RC_MAX],
            arrow_addclass: vec![0; CLASS_MAX],
            arrow_addsize: vec![0; SZ_MAX],
            magic_addele: vec![0; ELE_MAX],
            magic_addele_script: vec![0; ELE_MAX],
            magic_addrace: vec![0; RC_MAX],
            magic_addclass: vec![0; CLASS_MAX],
            magic_addsize: vec![0; SZ_MAX],
            magic_atk_ele: vec![0; ELE_MAX],
            weapon_subsize: vec![0; SZ_MAX],
            magic_subsize: vec![0; SZ_MAX],
            critaddrace: vec![0; RC_MAX],
            expaddrace: vec![0; RC_MAX],
            expaddclass: vec![0; CLASS_MAX],
            ignore_mdef_by_race: vec![0; RC_MAX],
            ignore_mdef_by_class: vec![0; CLASS_MAX],
            ignore_def_by_race: vec![0; RC_MAX],
            ignore_def_by_class: vec![0; CLASS_MAX],
            sp_gain_race: vec![0; RC_MAX],
            magic_addrace2: vec![0; RC2_MAX],
            ignore_mdef_by_race2: vec![0; RC2_MAX],
            dropaddrace: vec![0; RC_MAX],
            dropaddclass: vec![0; CLASS_MAX],
            magic_subdefele: vec![0; ELE_MAX],
            ignore_res_by_race: vec![0; RC_MAX],
            ignore_mres_by_race: vec![0; RC_MAX],
        }
    }
}

/// pc.hpp s_autospell.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct Autospell {
    pub id: u16,
    pub lv: u16,
    pub trigger_skill: u16,
    pub rate: i16,
    pub battle_flag: i16,
    pub card_id: u32,
    pub flag: u8,
    pub lock: bool,
}

/// pc.hpp s_addeffect.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct AddEffect {
    pub sc: i32,
    pub rate: i32,
    pub arrow_rate: i16,
    pub flag: u8,
    pub duration: u32,
}

/// pc.hpp s_addeffectonskill.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct AddEffectOnSkill {
    pub sc: i32,
    pub rate: i32,
    pub skill_id: i16,
    pub target: u8,
    pub duration: u32,
}

/// pc.hpp s_add_drop.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct AddDrop {
    pub nameid: u32,
    pub group: u16,
    pub rate: i32,
    pub race: i16,
    pub class_: u16,
}

/// pc.hpp s_vanish_bonus.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct VanishBonus {
    pub rate: i16,
    pub per: i16,
    pub flag: i32,
}

/// pc.hpp s_autobonus (`active` = the pc_endautobonus timer, 0 = INVALID_TIMER).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct Autobonus {
    pub rate: i16,
    pub atk_type: u16,
    pub duration: u32,
    pub bonus_script: String,
    pub other_script: Option<String>,
    pub active: u64,
    pub pos: u32,
}

/// pc.hpp s_regen (bHPRegen / bSPLossRate … lists).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct SRegen {
    pub value: i16,
    pub rate: i32,
    pub tick: i64,
}

/// map_session_data def_set_race / mdef_set_race / norecover_state_race entries.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct SetRace {
    pub value: i16,
    pub rate: i32,
    pub tick: i32,
}

/// map_session_data::s_bonus_vanish_gain.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct VanishGain {
    pub rate: i16,
    pub per: i16,
}

/// The std::vector bonus lists of map_session_data that status_calc_pc clears.
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct BonusLists {
    pub autospell: Vec<Autospell>,
    pub autospell2: Vec<Autospell>,
    pub autospell3: Vec<Autospell>,
    pub addeff: Vec<AddEffect>,
    pub addeff_atked: Vec<AddEffect>,
    pub addeff_onskill: Vec<AddEffectOnSkill>,
    pub skillatk: Vec<ItemBonus>,
    pub skillusesprate: Vec<ItemBonus>,
    pub skillusesp: Vec<ItemBonus>,
    pub skillheal: Vec<ItemBonus>,
    pub skillheal2: Vec<ItemBonus>,
    pub skillblown: Vec<ItemBonus>,
    pub skillcastrate: Vec<ItemBonus>,
    pub skillfixcastrate: Vec<ItemBonus>,
    pub subskill: Vec<ItemBonus>,
    pub skillcooldown: Vec<ItemBonus>,
    pub skillfixcast: Vec<ItemBonus>,
    pub skillvarcast: Vec<ItemBonus>,
    pub skilldelay: Vec<ItemBonus>,
    pub itemhealrate: Vec<ItemBonus>,
    pub add_def: Vec<ItemBonus>,
    pub add_mdef: Vec<ItemBonus>,
    pub add_mdmg: Vec<ItemBonus>,
    pub reseff: Vec<ItemBonus>,
    pub itemgrouphealrate: Vec<ItemBonus>,
    pub itemsphealrate: Vec<ItemBonus>,
    pub itemgroupsphealrate: Vec<ItemBonus>,
    pub add_drop: Vec<AddDrop>,
    pub subele2: Vec<AddEle2>,
    pub sp_vanish: Vec<VanishBonus>,
    pub hp_vanish: Vec<VanishBonus>,
    pub subrace3: Vec<AddRace2>,
    pub hp_loss: Vec<SRegen>,
    pub sp_loss: Vec<SRegen>,
    pub hp_regen: Vec<SRegen>,
    pub sp_regen: Vec<SRegen>,
    pub percent_hp_regen: Vec<SRegen>,
    pub percent_sp_regen: Vec<SRegen>,
}

/// The "zeroed structures" of map_session_data (def_set_race … sp_vanish_race), RC_MAX entries each.
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct RaceBonus {
    pub def_set_race: Vec<SetRace>,
    pub mdef_set_race: Vec<SetRace>,
    pub norecover_state_race: Vec<SetRace>,
    pub hp_vanish_race: Vec<VanishGain>,
    pub sp_vanish_race: Vec<VanishGain>,
}

impl Default for RaceBonus {
    fn default() -> Self {
        RaceBonus {
            def_set_race: vec![SetRace::default(); RC_MAX],
            mdef_set_race: vec![SetRace::default(); RC_MAX],
            norecover_state_race: vec![SetRace::default(); RC_MAX],
            hp_vanish_race: vec![VanishGain::default(); RC_MAX],
            sp_vanish_race: vec![VanishGain::default(); RC_MAX],
        }
    }
}

/// map_session_data::s_bonus (zeroed at the start of every status_calc_pc).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct Bonus {
    pub hp: i32,
    pub sp: i32,
    pub ap: i32,
    pub atk_rate: i32,
    pub arrow_atk: i32,
    pub arrow_ele: i32,
    pub arrow_cri: i32,
    pub arrow_hit: i32,
    pub nsshealhp: i32,
    pub nsshealsp: i32,
    pub critical_def: i32,
    pub double_rate: i32,
    pub short_attack_atk_rate: i32,
    pub long_attack_atk_rate: i32,
    pub near_attack_def_rate: i32,
    pub long_attack_def_rate: i32,
    pub magic_def_rate: i32,
    pub misc_def_rate: i32,
    pub ignore_mdef_ele: i32,
    pub ignore_mdef_race: i32,
    pub ignore_mdef_class: i32,
    pub perfect_hit: i32,
    pub perfect_hit_add: i32,
    pub get_zeny_rate: i32,
    pub get_zeny_num: i32,
    pub double_add_rate: i32,
    pub short_weapon_damage_return: i32,
    pub long_weapon_damage_return: i32,
    pub reduce_damage_return: i32,
    pub magic_damage_return: i32,
    pub break_weapon_rate: i32,
    pub break_armor_rate: i32,
    pub crit_atk_rate: i32,
    pub non_crit_atk_rate: i32,
    pub crit_def_rate: i32,
    pub classchange: i32,
    pub speed_rate: i32,
    pub speed_add_rate: i32,
    pub aspd_add: i32,
    pub itemhealrate2: i32,
    pub itemsphealrate2: i32,
    pub shieldmdef: i32,
    pub setitem_hash: u32,
    pub setitem_hash2: u32,
    pub splash_range: i16,
    pub splash_add_range: i16,
    pub add_steal_rate: i16,
    pub add_heal_rate: i32,
    pub add_heal2_rate: i32,
    pub sp_gain_value: i32,
    pub hp_gain_value: i32,
    pub magic_sp_gain_value: i32,
    pub magic_hp_gain_value: i32,
    pub long_sp_gain_value: i32,
    pub long_hp_gain_value: i32,
    pub unbreakable: u16,
    pub unbreakable_equip: u16,
    pub unstripable_equip: u16,
    pub fixcastrate: i32,
    pub varcastrate: i32,
    pub delayrate: i32,
    pub add_fixcast: i32,
    pub add_varcast: i32,
    pub ematk: i32,
    pub ematk_hidden: i32,
    pub eatk: i32,
    pub absorb_dmg_maxhp: u8,
    pub absorb_dmg_maxhp2: u8,
    pub critical_rangeatk: i16,
    pub weapon_atk_rate: i16,
    pub weapon_matk_rate: i16,
    pub skill_ratio: i32,
}

/// map_session_data special_state (recalculated in each status_calc_pc).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct SpecialState {
    pub no_weapon_damage: u8,
    pub no_magic_damage: u8,
    pub no_misc_damage: u8,
    pub restart_full_recover: bool,
    pub no_castcancel: bool,
    pub no_castcancel2: bool,
    pub no_sizefix: bool,
    /// no_gemstone : 2.
    pub no_gemstone: u8,
    pub intravision: bool,
    pub perfect_hiding: bool,
    pub no_knockback: bool,
    pub bonus_coma: bool,
    pub no_mado_fuel: bool,
    pub no_walk_delay: bool,
}

/// map_session_data's rate fields (castrate … crate_rate; "not zeroed", reset to 100 by status_calc_pc).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct Rates {
    pub castrate: i32,
    pub hprate: i32,
    pub sprate: i32,
    pub aprate: i32,
    pub dsprate: i32,
    pub hprecov_rate: i32,
    pub sprecov_rate: i32,
    pub matk_rate: i32,
    pub critical_rate: i32,
    pub hit_rate: i32,
    pub flee_rate: i32,
    pub flee2_rate: i32,
    pub def_rate: i32,
    pub def2_rate: i32,
    pub mdef_rate: i32,
    pub mdef2_rate: i32,
    pub patk_rate: i32,
    pub smatk_rate: i32,
    pub res_rate: i32,
    pub mres_rate: i32,
    pub hplus_rate: i32,
    pub crate_rate: i32,
}

/// status.hpp struct regen_data_sub (its `tick` part lives in [`PcRegenTick`]).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct RegenDataSub {
    pub hp: u16,
    pub sp: u16,
    pub rate_hp: u16,
    pub rate_sp: u16,
}

/// status.hpp struct regen_data (its `tick` part lives in [`PcRegenTick`]; sregen / ssregen are the character's
/// own [`PcStatus::sregen`] / [`PcStatus::ssregen`], set at the first calculation).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct RegenData {
    pub flag: u8,
    pub hp: u16,
    pub sp: u16,
    pub shp: u16,
    pub ssp: u16,
    pub rate_hp: u16,
    pub rate_sp: u16,
    pub rate_shp: u16,
    pub rate_ssp: u16,
    pub walk: bool,
    pub gc: bool,
    pub overweight: bool,
    /// state.block : 2 (1: HP, 2: SP).
    pub block: u8,
    /// regen->sregen / regen->ssregen set (status_calc_pc_sub SCO_FIRST).
    pub has_sub: bool,
}

/// map_session_data's status-system part for an online character.
#[table(accessor = pc_status)]
#[derive(Clone, Debug)]
pub struct PcStatus {
    #[primary_key]
    pub account_id: u32,
    #[unique]
    pub char_id: u32,
    /// sd->class_ (MAPID_*).
    pub class_: u64,
    pub base_status: StatusData,
    pub battle_status: StatusData,
    pub regen: RegenData,
    pub sregen: RegenDataSub,
    pub ssregen: RegenDataSub,
    pub right_weapon: WeaponData,
    pub left_weapon: WeaponData,
    pub indexed_bonus: IndexedBonus,
    pub lists: BonusLists,
    pub autobonus: Vec<Autobonus>,
    pub autobonus2: Vec<Autobonus>,
    pub autobonus3: Vec<Autobonus>,
    pub races: RaceBonus,
    pub bonus: Bonus,
    pub rates: Rates,
    pub special_state: SpecialState,
    /// sd->state.lr_flag (LR_FLAG_*), set while an equipment script of the left hand / arrow / shield runs.
    pub lr_flag: u8,
    /// sd->add_max_weight (bAddMaxWeight; zeroed by status_calc_pc). sd->weight / max_weight / cart_weight /
    /// cart_num / cart_weight_max, sd->equip_index, weapontype1 / 2 and sd->combos are the items system's `pc_items`
    /// row (inventory.rs); sd->status.weapon is the character row.
    pub add_max_weight: u32,
    /// sd->die_counter (PC_DIE_COUNTER; -1 until pc_reg_received).
    pub die_counter: i32,
    pub permanent_speed: bool,
    /// sd->state.rest (TK_HPTIME / TK_SPTIME).
    pub rest: bool,
    pub hold_recalc: bool,
    pub delayed_damage: i32,
    /// Obsolete: sd->soulball is the skill system's `pc_skill` row (skill/state.rs); battle/pc_death.rs still reads
    /// this copy (always 0). Drop it when that read moves.
    pub soulball: i32,
    /// sd->state.doridori.
    pub doridori: bool,
    /// sd->state.gangsterparadise (skill_sit_in / skill_sit_out).
    pub gangsterparadise: bool,
}

/// status.hpp regen_data::tick and regen_data_sub::tick of a character (moved by natural heal every 500 ms).
#[table(accessor = pc_regen_tick)]
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PcRegenTick {
    #[primary_key]
    pub account_id: u32,
    /// regen.tick.hp / .sp: time of the last natural recovery.
    pub hp: Tick,
    pub sp: Tick,
    /// regen.tick.shp / .ssp (unused by rAthena's players).
    pub shp: u32,
    pub ssp: u32,
    pub s_hp: u32,
    pub s_sp: u32,
    pub ss_hp: u32,
    pub ss_sp: u32,
}

/// status.hpp struct status_change_entry, one row per active status change of any unit.
#[table(accessor = sc_entry, index(accessor = by_bl, btree(columns = [bl_id])))]
#[derive(Clone, Debug, PartialEq)]
pub struct ScEntry {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub bl_id: u32,
    /// sc_type.
    pub sc_type: i32,
    /// status_change_timer (0 = INVALID_TIMER, infinite).
    pub timer: u64,
    pub val1: i32,
    pub val2: i32,
    pub val3: i32,
    pub val4: i32,
}

/// sc_display_entry (status_display_add for SCF_DISPLAYPC / SCF_DISPLAYNPC).
#[derive(SpacetimeType, Clone, Debug, Default, PartialEq)]
pub struct ScDisplay {
    pub sc_type: i32,
    pub val1: i32,
    pub val2: i32,
    pub val3: i32,
}

/// class status_change's other members (opt1 / opt2 / opt3 / option live in the unit row's `sc`).
#[table(accessor = sc_unit)]
#[derive(Clone, Debug, PartialEq)]
pub struct ScUnit {
    #[primary_key]
    pub bl_id: u32,
    /// cant.* as SCS_ bits (1 << e_scs_flag of the NO* flag).
    pub cant: u32,
    pub last_effect: i32,
    pub last_effect_timer: u64,
    pub comet_x: i16,
    pub comet_y: i16,
    /// sd->sc_display.
    pub display: Vec<ScDisplay>,
    /// The iteration order of status_change::data (a libstdc++ unordered_map: sc_umap.rs), its bucket count and
    /// `_M_next_resize`.
    pub umap_order: Vec<i32>,
    pub umap_buckets: u64,
    pub umap_next_resize: u64,
}

/// status_change::status_change (status.cpp:1180): `lastEffect = SC_NONE; lastEffectTimer = INVALID_TIMER`, the rest
/// zero. (SC_NONE is -1: a zero would read as SC_STONE, which the Fail lists name.)
impl Default for ScUnit {
    fn default() -> Self {
        ScUnit {
            bl_id: 0,
            cant: 0,
            last_effect: super::consts::SC_NONE,
            last_effect_timer: 0,
            comet_x: 0,
            comet_y: 0,
            display: Vec::new(),
            umap_order: Vec::new(),
            umap_buckets: 1,
            umap_next_resize: 0,
        }
    }
}

/// char-server `sc_data`: a character's saved status changes (chrif_save_scdata writes them at logout, the next login
/// reads them; they stay until the next save replaces them). The table is MyISAM with static rows: `pos` is the
/// record slot, whose order is the order the char server's `SELECT ... WHERE account_id AND char_id` returns (a full
/// scan), and `sc_data_slots` its free list.
#[table(accessor = sc_saved, index(accessor = by_char, btree(columns = [char_id])))]
#[derive(Clone, Debug)]
pub struct ScSaved {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    /// The MyISAM record slot.
    pub pos: u64,
    pub char_id: u32,
    pub sc_type: i32,
    /// Remaining duration (-1 = INFINITE_TICK).
    pub tick: i64,
    pub val1: i32,
    pub val2: i32,
    pub val3: i32,
    pub val4: i32,
}

/// The `sc_data` table's MyISAM record slots: the next never-used slot and the deleted ones (the last deleted is
/// reused first, also when the table is empty: checked on the oracle's MariaDB 10.11).
#[table(accessor = sc_data_slots)]
#[derive(Clone, Debug, Default)]
pub struct ScDataSlots {
    #[primary_key]
    pub id: u8,
    pub next: u64,
    pub free: Vec<u64>,
}

/// pc.hpp s_bonus_script_entry (and the char-server `bonus_script` table: rows stay while the character is offline,
/// `tick` then holding the remaining time). Rows keep insertion order by id (sd->bonus_script.head list order).
#[table(accessor = bonus_script, index(accessor = by_char, btree(columns = [char_id])))]
#[derive(Clone, Debug)]
pub struct BonusScript {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub char_id: u32,
    pub script: String,
    /// Online: the expiry tick (timer due); offline: the remaining duration.
    pub tick: Tick,
    pub flag: u16,
    pub icon: i32,
    /// 0 ignore, 1 buff, 2 debuff.
    pub kind: u8,
    /// pc_bonus_script_timer (0 = INVALID_TIMER).
    pub tid: u64,
    pub online: bool,
}

/// status.cpp globals: natural_heal_prev_tick and the natural heal interval timer.
#[table(accessor = status_world)]
#[derive(Clone, Debug, Default)]
pub struct StatusWorld {
    #[primary_key]
    pub key: u8,
    pub natural_heal_timer: u64,
    pub natural_heal_prev_tick: Tick,
    pub natural_heal_diff_tick: Tick,
}

impl PcStatus {
    pub fn new(account_id: u32, char_id: u32, class_: u64) -> Self {
        PcStatus {
            account_id,
            char_id,
            class_,
            base_status: StatusData { speed: DEFAULT_WALK_SPEED as u16, ..Default::default() },
            battle_status: StatusData { speed: DEFAULT_WALK_SPEED as u16, ..Default::default() },
            regen: RegenData::default(),
            sregen: RegenDataSub::default(),
            ssregen: RegenDataSub::default(),
            right_weapon: WeaponData::default(),
            left_weapon: WeaponData::default(),
            indexed_bonus: IndexedBonus::default(),
            lists: BonusLists::default(),
            autobonus: Vec::new(),
            autobonus2: Vec::new(),
            autobonus3: Vec::new(),
            races: RaceBonus::default(),
            bonus: Bonus::default(),
            rates: Rates::default(),
            special_state: SpecialState::default(),
            lr_flag: LR_FLAG_NONE,
            add_max_weight: 0,
            die_counter: -1,
            permanent_speed: false,
            rest: false,
            hold_recalc: false,
            delayed_damage: 0,
            soulball: 0,
            doridori: false,
            gangsterparadise: false,
        }
    }
}

// ---------------------------------------------------------------------------------------------- the working copy

thread_local! {
    /// The rows of characters whose status_calc_pc is running (see the module doc).
    static WORK: RefCell<Vec<PcStatus>> = const { RefCell::new(Vec::new()) };
}

/// The character's status row (the in-calculation copy while its status_calc_pc runs).
pub fn sd_get(c: &Ctx, aid: u32) -> Option<PcStatus> {
    if let Some(sd) = WORK.with(|w| w.borrow().iter().find(|s| s.account_id == aid).cloned()) {
        return Some(sd);
    }
    c.db().pc_status().account_id().find(aid)
}

/// Stores the character's status row (into the in-calculation copy while its status_calc_pc runs).
pub fn sd_put(c: &Ctx, sd: &PcStatus) {
    let held = WORK.with(|w| {
        let mut w = w.borrow_mut();
        match w.iter_mut().find(|s| s.account_id == sd.account_id) {
            Some(slot) => {
                *slot = sd.clone();
                true
            }
            None => false,
        }
    });
    if held {
        return;
    }
    if c.db().pc_status().account_id().find(sd.account_id).is_some() {
        c.db().pc_status().account_id().update(sd.clone());
    }
}

/// Runs `f` on the character's status row and stores it back. None when the character has no status row.
pub fn with_sd<R>(c: &Ctx, aid: u32, f: impl FnOnce(&mut PcStatus) -> R) -> Option<R> {
    let mut sd = sd_get(c, aid)?;
    let r = f(&mut sd);
    sd_put(c, &sd);
    Some(r)
}

/// Whether the character's row is held in memory by a running calculation.
pub fn is_held(aid: u32) -> bool {
    WORK.with(|w| w.borrow().iter().any(|s| s.account_id == aid))
}

/// Holds the row in memory (the outermost status_calc_pc of this character starts).
pub fn hold(c: &Ctx, aid: u32) -> bool {
    if is_held(aid) {
        return false;
    }
    let Some(sd) = c.db().pc_status().account_id().find(aid) else { return false };
    WORK.with(|w| w.borrow_mut().push(sd));
    true
}

/// Writes the held row back and releases it (the outermost status_calc_pc ended).
pub fn release(c: &Ctx, aid: u32) {
    let sd = WORK.with(|w| {
        let mut w = w.borrow_mut();
        let i = w.iter().position(|s| s.account_id == aid)?;
        Some(w.remove(i))
    });
    if let Some(sd) = sd {
        if c.db().pc_status().account_id().find(aid).is_some() {
            c.db().pc_status().account_id().update(sd);
        }
    }
}

/// Drops every held row without writing (a new reducer call starts from the tables).
pub fn reset_work() {
    WORK.with(|w| w.borrow_mut().clear());
    super::calc::reset_globals();
    super::sc::reset_caches();
}

// ---------------------------------------------------------------------------------------------- regen ticks

pub fn regen_tick(c: &Ctx, aid: u32) -> PcRegenTick {
    c.db().pc_regen_tick().account_id().find(aid).unwrap_or(PcRegenTick { account_id: aid, ..Default::default() })
}

pub fn put_regen_tick(c: &Ctx, t: &PcRegenTick) {
    match c.db().pc_regen_tick().account_id().find(t.account_id) {
        Some(old) if old == *t => {}
        Some(_) => {
            c.db().pc_regen_tick().account_id().update(t.clone());
        }
        None => {
            c.db().pc_regen_tick().insert(t.clone());
        }
    }
}

// ---------------------------------------------------------------------------------------------- world

pub fn world(c: &Ctx) -> StatusWorld {
    c.db().status_world().key().find(0).unwrap_or_default()
}

pub fn put_world(c: &Ctx, w: StatusWorld) {
    if c.db().status_world().key().find(0).is_some() {
        c.db().status_world().key().update(w);
    } else {
        c.db().status_world().insert(w);
    }
}
