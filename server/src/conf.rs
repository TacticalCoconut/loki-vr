//! battle_config values (conf/battle/*.conf, rAthena e985006 defaults) and the local server's conf/import overrides.
//! Each names its file and key.
//!
//! A `pub fn` here reads the run-time battle_config (battle_config/mod.rs `get`: a host or @setbattleflag can change
//! it); a `pub const` is a value the module still holds as a build constant (port wave I converts them a group at a
//! time: server/ARCHITECTURE.md has both lists).

use crate::battle_config::{self as bc, Bc};

/// client.conf `area_size: 14`: battle_config.area_size, rAthena's `AREA_SIZE` (map.hpp:66 `#define AREA_SIZE
/// battle_config.area_size`; battle.cpp:8553: default 14, 0 to INT_MAX): how far units are seen and area packets
/// reach. A run-time setting since port wave I: Loki Host writes its "view range" as `area_size` into
/// conf/import/battle_conf.txt (tools/lokihost/Core/HostSetup.cs:52-54; its default is 30, the design's choice of
/// 2026-10-01); here the owner gives the same line with `set_battle_conf` (battle_config/mod.rs).
///
/// Capped at [`AREA_SIZE_CAP`]: the module's coordinates are 16-bit as rAthena's are (map_foreachinallarea takes
/// int16 bounds, so `x + AREA_SIZE` wraps there for a value near 32767), and no map is wider than 512 cells
/// (map.hpp:82 MAX_MAP_SIZE; the largest embedded map is 440): from the cap on, every range already is the whole map.
pub fn area_size() -> i16 {
    bc::get(Bc::AREA_SIZE).clamp(0, AREA_SIZE_CAP as i32) as i16
}
/// See [`area_size`].
pub const AREA_SIZE_CAP: i16 = 4096;
/// client.conf `max_walk_path: 17` — the longest path a click may start.
pub fn max_walk_path() -> i32 {
    bc::get(Bc::MAX_WALK_PATH)
}
/// misc.conf `official_cell_stack_limit: 1`.
pub fn official_cell_stack_limit() -> i32 {
    bc::get(Bc::OFFICIAL_CELL_STACK_LIMIT)
}
/// player.conf `prevent_logout: 10000` (ms after attacking, being hit or casting before one may leave).
pub fn prevent_logout() -> i64 {
    bc::get(Bc::PREVENT_LOGOUT) as i64
}
/// player.conf `prevent_logout_trigger: 14` (PLT_ATTACK | PLT_SKILL | PLT_DAMAGE; not on login).
pub fn prevent_logout_trigger() -> i32 {
    bc::get(Bc::PREVENT_LOGOUT_TRIGGER)
}
pub const PLT_LOGIN: i32 = 0x1;
/// client.conf `min_chat_delay: 0`.
pub fn min_chat_delay() -> i64 {
    bc::get(Bc::MIN_CHAT_DELAY) as i64
}
/// skill.conf `basic_skill_check: yes`.
pub fn basic_skill_check() -> bool {
    bc::get(Bc::BASIC_SKILL_CHECK) != 0
}
/// client.conf `client_reshuffle_dice: yes`.
pub fn client_reshuffle_dice() -> bool {
    bc::get(Bc::CLIENT_RESHUFFLE_DICE) != 0
}
/// client.conf `spawn_direction: no`.
pub fn spawn_direction() -> bool {
    bc::get(Bc::SPAWN_DIRECTION) != 0
}
/// player.conf `revive_onwarp: yes`.
pub fn revive_onwarp() -> bool {
    bc::get(Bc::REVIVE_ONWARP) != 0
}
/// client.conf `drop_connection_on_quit: no`.
pub fn drop_connection_on_quit() -> bool {
    bc::get(Bc::DROP_CONNECTION_ON_QUIT) != 0
}
/// client.conf `max_lv: 99`, `aura_lv: 99`, `client_limit_unit_lv: 0`.
pub fn max_lv() -> i32 {
    bc::get(Bc::MAX_LV)
}
pub fn aura_lv() -> i32 {
    bc::get(Bc::AURA_LV)
}
pub fn client_limit_unit_lv() -> u16 {
    bc::get(Bc::CLIENT_LIMIT_UNIT_LV) as u16
}
/// client.conf `display_version: yes`, `motd_type: 0`.
pub fn display_version() -> bool {
    bc::get(Bc::DISPLAY_VERSION) != 0
}
/// monster.conf `monster_hp_bars_info: yes`.
pub fn monster_hp_bars_info() -> bool {
    bc::get(Bc::MONSTER_HP_BARS_INFO) != 0
}
/// client.conf `hide_cloaked_units: 0`.
pub fn hide_cloaked_units() -> u16 {
    bc::get(Bc::HIDE_CLOAKED_UNITS) as u16
}
/// player.conf `player_invincible_time: 3000`.
pub fn pc_invincible_time() -> i64 {
    bc::get(Bc::PLAYER_INVINCIBLE_TIME) as i64
}
/// misc.conf `teleport_on_portal: no`.
pub fn teleport_on_portal() -> bool {
    bc::get(Bc::TELEPORT_ON_PORTAL) != 0
}
/// client.conf `min_hair_style` … `max_cloth_color` (0-42, 0-8, 0-7).
pub fn min_hair_style() -> i32 {
    bc::get(Bc::MIN_HAIR_STYLE)
}
pub fn max_hair_style() -> i32 {
    bc::get(Bc::MAX_HAIR_STYLE)
}
pub fn min_hair_color() -> i32 {
    bc::get(Bc::MIN_HAIR_COLOR)
}
pub fn max_hair_color() -> i32 {
    bc::get(Bc::MAX_HAIR_COLOR)
}
pub fn min_cloth_color() -> i32 {
    bc::get(Bc::MIN_CLOTH_COLOR)
}
pub fn max_cloth_color() -> i32 {
    bc::get(Bc::MAX_CLOTH_COLOR)
}
/// monster.conf `dynamic_mobs: yes`.
pub fn dynamic_mobs() -> bool {
    bc::get(Bc::DYNAMIC_MOBS) != 0
}
/// party.conf `display_party_name: yes`.
pub fn display_party_name() -> bool {
    bc::get(Bc::DISPLAY_PARTY_NAME) != 0
}
/// player.conf `idletime_option: 0x7C1F`.
pub fn idletime_option() -> i32 {
    bc::get(Bc::IDLETIME_OPTION)
}
/// player.conf `loose_ap_on_map: yes` (a map change onto a PvP / GvG / BG map empties AP).
pub fn loose_ap_on_map() -> bool {
    bc::get(Bc::LOOSE_AP_ON_MAP) != 0
}

/// conf/import/battle_conf.txt on the local server: `loki_sit_regen_rate: 25`. Loki's own setting, added to
/// battle_data by data/overlay/rathena-loki.patch (default 1, 1 to 1000): natural HP / SP recovery per tick is
/// multiplied by it while sitting (status.cpp status_natural_heal in the patch). Read at run time.
pub fn loki_sit_regen_rate() -> i64 {
    bc::get(Bc::LOKI_SIT_REGEN_RATE) as i64
}

// conf/motd.txt (data/json/motd.json.z: its fgets pieces, written by tools/datagen.py build_motd from the text pack's
// rewrite when there is one, so the login message follows the server text overhaul as rAthena's does).
crate::dataset!(motd_file, "motd", Vec<String>);

/// pc_read_motd (pc.cpp:14633-14675): the lines pc_authok shows at login. `//` pieces are skipped, an empty line is
/// kept as " ", at most MOTD_LINE_SIZE (128) of them.
pub fn motd() -> impl Iterator<Item = &'static str> {
    motd_file().iter().filter(|l| !l.starts_with("//")).map(|l| if l.is_empty() { " " } else { l.as_str() }).take(128)
}

/// monster.conf `mob_respawn_time: 1000`, `mob_spawn_variance: 1`, `mob_count_rate: 100`, `force_random_spawn: no`,
/// `randomize_center_cell: yes`, `override_mob_names: 0`; misc.conf `warp_point_debug: no`; monster.conf `mob_warp: 0`.
pub fn mob_respawn_time() -> i64 {
    bc::get(Bc::MOB_RESPAWN_TIME) as i64
}
pub fn mob_spawn_variance() -> i32 {
    bc::get(Bc::MOB_SPAWN_VARIANCE)
}
pub fn randomize_center_cell() -> bool {
    bc::get(Bc::RANDOMIZE_CENTER_CELL) != 0
}
pub fn mob_warp() -> i32 {
    bc::get(Bc::MOB_WARP)
}

// ---------------------------------------------------------------------------------------------- status (status.cpp / pc.cpp)

/// battle/status.conf `status_cast_cancel: 0`, `pc_status_def_rate: 100`, `mob_status_def_rate: 100`,
/// `pc_max_status_def: 100`, `mob_max_status_def: 100`.
pub fn sc_castcancel() -> u16 {
    bc::get(Bc::STATUS_CAST_CANCEL) as u16
}
pub fn pc_sc_def_rate() -> i32 {
    bc::get(Bc::PC_STATUS_DEF_RATE)
}
pub fn mob_sc_def_rate() -> i32 {
    bc::get(Bc::MOB_STATUS_DEF_RATE)
}
pub fn pc_max_sc_def() -> i32 {
    bc::get(Bc::PC_MAX_STATUS_DEF)
}
pub fn mob_max_sc_def() -> i32 {
    bc::get(Bc::MOB_MAX_STATUS_DEF)
}
/// battle/battle.conf `undead_detect_type: 0`; battle/skill.conf `gtb_sc_immunity: 50` (battle.cpp default 50).
pub fn undead_detect_type() -> i32 {
    bc::get(Bc::UNDEAD_DETECT_TYPE)
}
pub fn gtb_sc_immunity() -> i32 {
    bc::get(Bc::GTB_SC_IMMUNITY)
}
/// battle/misc.conf `manner_system: 15`.
pub fn manner_system() -> i32 {
    bc::get(Bc::MANNER_SYSTEM)
}
/// battle/player.conf `hp_rate: 100`, `sp_rate: 100`, `ap_rate: 100`, `max_hp_lv99: 330000`, `max_hp_lv150: 660000`,
/// `max_hp: 1100000`, `max_sp: 1000000`, `max_ap: 1000`, `restart_hp_rate: 0`, `restart_sp_rate: 0`,
/// `restart_ap_rate: 0`, `keep_ap_on_logout: yes`.
pub fn hp_rate() -> i32 {
    bc::get(Bc::HP_RATE)
}
pub fn sp_rate() -> i32 {
    bc::get(Bc::SP_RATE)
}
pub fn ap_rate() -> i32 {
    bc::get(Bc::AP_RATE)
}
pub fn max_hp_lv99() -> u32 {
    bc::get(Bc::MAX_HP_LV99) as u32
}
pub fn max_hp_lv150() -> u32 {
    bc::get(Bc::MAX_HP_LV150) as u32
}
pub fn max_hp() -> u32 {
    bc::get(Bc::MAX_HP) as u32
}
pub fn max_sp() -> u32 {
    bc::get(Bc::MAX_SP) as u32
}
pub fn max_ap() -> u32 {
    bc::get(Bc::MAX_AP) as u32
}
pub fn restart_hp_rate() -> i64 {
    bc::get(Bc::RESTART_HP_RATE) as i64
}
pub fn restart_sp_rate() -> i64 {
    bc::get(Bc::RESTART_SP_RATE) as i64
}
pub fn restart_ap_rate() -> i64 {
    bc::get(Bc::RESTART_AP_RATE) as i64
}
pub fn keep_ap_on_logout() -> bool {
    bc::get(Bc::KEEP_AP_ON_LOGOUT) != 0
}
/// battle/player.conf `summoner_race: 11` (RC_PLAYER_DORAM), `summoner_size: 0` (SZ_SMALL), `character_size: 0`.
pub fn summoner_race() -> u8 {
    bc::get(Bc::SUMMONER_RACE) as u8
}
pub fn summoner_size() -> u8 {
    bc::get(Bc::SUMMONER_SIZE) as u8
}
pub fn character_size() -> i32 {
    bc::get(Bc::CHARACTER_SIZE)
}
/// battle/player.conf `max_aspd: 190`, `max_third_aspd: 193`, `max_extended_aspd: 193`, `max_summoner_aspd: 193`,
/// converted by battle.cpp battle_adjust_conf to the minimum delay: (2000 - aspd * 10) * 2.
pub fn max_aspd() -> i32 {
    bc::get(Bc::MAX_ASPD)
}
pub fn max_third_aspd() -> i32 {
    bc::get(Bc::MAX_THIRD_ASPD)
}
pub fn max_extended_aspd() -> i32 {
    bc::get(Bc::MAX_EXTENDED_ASPD)
}
pub fn max_summoner_aspd() -> i32 {
    bc::get(Bc::MAX_SUMMONER_ASPD)
}
/// battle/player.conf `max_walk_speed: 300`, converted by battle_adjust_conf: 100 * DEFAULT_WALK_SPEED / 300.
pub fn max_walk_speed_conf() -> i32 {
    bc::get(Bc::MAX_WALK_SPEED)
}
/// battle/player.conf `player_damage_delay_rate: 100` (pc_damage_delay_rate).
pub fn pc_damage_delay_rate() -> i32 {
    bc::get(Bc::PLAYER_DAMAGE_DELAY_RATE)
}
/// battle/battle.conf `enable_baseatk_renewal: 0x29F`, `enable_critical: 17`, `enable_perfect_flee: 1`,
/// `critical_rate: 100`; battle/battle.conf `left_cardfix_to_right: yes`.
pub fn enable_baseatk_renewal() -> u16 {
    bc::get(Bc::ENABLE_BASEATK_RENEWAL) as u16
}
pub fn enable_critical() -> u16 {
    bc::get(Bc::ENABLE_CRITICAL) as u16
}
pub fn enable_perfect_flee() -> u16 {
    bc::get(Bc::ENABLE_PERFECT_FLEE) as u16
}
pub fn left_cardfix_to_right() -> bool {
    bc::get(Bc::LEFT_CARDFIX_TO_RIGHT) != 0
}
/// battle/player.conf `max_cart_weight: 8000` (× 10 in battle_adjust_conf).
pub fn max_cart_weight() -> i32 {
    bc::get(Bc::MAX_CART_WEIGHT)
}
/// battle/player.conf `natural_healhp_interval: 6000`, `natural_healsp_interval: 8000`,
/// `natural_heal_skill_interval: 10000`; RENEWAL default `natural_heal_weight_rate: 70` (commented out in the conf),
/// `major_overweight_rate: 90`.
pub fn natural_healhp_interval() -> i64 {
    bc::get(Bc::NATURAL_HEALHP_INTERVAL) as i64
}
pub fn natural_healsp_interval() -> i64 {
    bc::get(Bc::NATURAL_HEALSP_INTERVAL) as i64
}
pub fn natural_heal_skill_interval() -> u32 {
    bc::get(Bc::NATURAL_HEAL_SKILL_INTERVAL) as u32
}
pub fn natural_heal_weight_rate() -> u32 {
    bc::get(Bc::NATURAL_HEAL_WEIGHT_RATE) as u32
}
pub fn major_overweight_rate() -> u32 {
    bc::get(Bc::MAJOR_OVERWEIGHT_RATE) as u32
}
/// battle/guild.conf `gvg_flee_penalty: 20`, battle/battleground.conf `bg_flee_penalty: 20`.
pub fn gvg_flee_penalty() -> i32 {
    bc::get(Bc::GVG_FLEE_PENALTY)
}
pub fn bg_flee_penalty() -> i32 {
    bc::get(Bc::BG_FLEE_PENALTY)
}
/// battle/player.conf `rental_mount_speed_boost: 25`.
pub fn rental_mount_speed_boost() -> i32 {
    bc::get(Bc::RENTAL_MOUNT_SPEED_BOOST)
}
/// battle/exp.conf `multi_level_up: no`, `multi_level_up_base: 0`, `multi_level_up_job: 0`, `max_exp_gain_rate: 0`,
/// `disp_experience: no`, `pvp_exp: yes`, `exp_calc_type: 0`; battle/gm.conf `atcommand_levelup_events: no`.
pub fn multi_level_up() -> bool {
    bc::get(Bc::MULTI_LEVEL_UP) != 0
}
pub fn multi_level_up_base() -> u32 {
    bc::get(Bc::MULTI_LEVEL_UP_BASE) as u32
}
pub fn multi_level_up_job() -> u32 {
    bc::get(Bc::MULTI_LEVEL_UP_JOB) as u32
}
pub fn max_exp_gain_rate() -> i64 {
    bc::get(Bc::MAX_EXP_GAIN_RATE) as i64
}
pub fn disp_experience() -> bool {
    bc::get(Bc::DISP_EXPERIENCE) != 0
}
pub fn pvp_exp() -> bool {
    bc::get(Bc::PVP_EXP) != 0
}
pub fn exp_calc_type() -> i32 {
    bc::get(Bc::EXP_CALC_TYPE)
}
pub fn atcommand_levelup_events() -> bool {
    bc::get(Bc::ATCOMMAND_LEVELUP_EVENTS) != 0
}
/// battle/items.conf `autospell_stacking: no`; battle/drops.conf `item_rate_adddrop: 100`, `item_drop_add_min: 1`,
/// `item_drop_add_max: 10000`.
pub fn autospell_stacking() -> bool {
    bc::get(Bc::AUTOSPELL_STACKING) != 0
}
pub fn item_rate_adddrop() -> i32 {
    bc::get(Bc::ITEM_RATE_ADDDROP)
}
pub fn item_drop_adddrop_min() -> i32 {
    bc::get(Bc::ITEM_DROP_ADD_MIN)
}
pub fn item_drop_adddrop_max() -> i32 {
    bc::get(Bc::ITEM_DROP_ADD_MAX)
}
/// battle/client.conf `display_status_timers: yes`, `display_hallucination: yes`.
pub fn display_status_timers() -> bool {
    bc::get(Bc::DISPLAY_STATUS_TIMERS) != 0
}
pub fn display_hallucination() -> bool {
    bc::get(Bc::DISPLAY_HALLUCINATION) != 0
}
/// battle/battle.conf `invincible.nodamage: no`, `infinite_endure: 0`; battle/misc.conf `clear_skills_on_death: 0`
/// (clear_unit_ondeath); battle/pet.conf `pet_lv_rate: 0`.
pub fn invincible_nodamage() -> bool {
    bc::get(Bc::INVINCIBLE_NODAMAGE) != 0
}
pub fn infinite_endure() -> u16 {
    bc::get(Bc::INFINITE_ENDURE) as u16
}
pub fn clear_unit_ondeath() -> u16 {
    bc::get(Bc::CLEAR_SKILLS_ON_DEATH) as u16
}
pub fn pet_lv_rate() -> i32 {
    bc::get(Bc::PET_LV_RATE)
}
/// player.conf prevent_logout_trigger bit PLT_DAMAGE (pc_damage refreshes canlog_tick).
pub const PLT_DAMAGE: i32 = 0x8;
/// status.conf `debuff_on_logout: 0`.
pub fn debuff_on_logout() -> i32 {
    bc::get(Bc::DEBUFF_ON_LOGOUT)
}
/// misc.conf `pk_mode: 0`.
pub fn pk_mode() -> i32 {
    bc::get(Bc::PK_MODE)
}
/// player.conf `transcendent_status_points: 52`, `trait_points_job_change: 7`.
pub fn transcendent_status_points() -> u32 {
    bc::get(Bc::TRANSCENDENT_STATUS_POINTS) as u32
}
pub fn trait_points_job_change() -> u32 {
    bc::get(Bc::TRAIT_POINTS_JOB_CHANGE) as u32
}
/// battle/feature.conf `feature.dynamicnpc_timeout: 60000`, `feature.dynamicnpc_rangex: 2`, `feature.dynamicnpc_rangey:
/// 2`, `feature.dynamicnpc_direction: no` (npc.cpp npc_duplicate_npc_for_player / npc_dynamicnpc_removal_timer).
pub fn feature_dynamicnpc_timeout() -> i64 {
    bc::get(Bc::FEATURE_DYNAMICNPC_TIMEOUT) as i64
}
pub fn feature_dynamicnpc_rangex() -> i16 {
    bc::get(Bc::FEATURE_DYNAMICNPC_RANGEX).clamp(-32768, 32767) as i16
}
pub fn feature_dynamicnpc_rangey() -> i16 {
    bc::get(Bc::FEATURE_DYNAMICNPC_RANGEY).clamp(-32768, 32767) as i16
}
pub fn feature_dynamicnpc_direction() -> bool {
    bc::get(Bc::FEATURE_DYNAMICNPC_DIRECTION) != 0
}
