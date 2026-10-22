//! The battle_config values the skill unit code reads (conf/battle/*.conf of rAthena e985006 after
//! battle_adjust_conf, as the reference map-server runs them). Each constant names its battle_data key; a unit test
//! (`tests.rs`) checks every value against `script/battle_flags.rs`, the reference server's whole table.

/// battle_config.traps_setting: skill.conf leaves `//traps_setting: 0` commented out, so battle.cpp battle_data's
/// RENEWAL default 2 applies (hidden traps are hidden on every map).
pub fn traps_setting() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::TRAPS_SETTING)
}
/// skill.conf `gvg_traps_target_all: 1` (battle_config.vs_traps_bctall, a BL_ mask: BL_PC).
pub fn vs_traps_bctall() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::GVG_TRAPS_TARGET_ALL) as u16
}
/// skill.conf `skill_wall_check: yes` (map_foreachinrange / map_foreachinarea check a shootable path).
pub fn skill_wall_check() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_WALL_CHECK) != 0
}
/// skill.conf `land_protector_behavior: 0` (the unit's cell decides, not the target's).
pub fn land_protector_behavior() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::LAND_PROTECTOR_BEHAVIOR) != 0
}
/// skill.conf `skill_reiteration: 0` (BL_ mask).
pub fn skill_reiteration() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_REITERATION) as u16
}
/// skill.conf `skill_nofootset: 1` (BL_ mask: BL_PC).
pub fn skill_nofootset() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_NOFOOTSET) as u16
}
/// skill.conf `land_skill_limit: 9` (BL_ mask: BL_PC | BL_HOM).
pub fn land_skill_limit() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::LAND_SKILL_LIMIT) as u16
}
/// skill.conf `chat_warpportal: no`.
pub fn chat_warpportal() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::CHAT_WARPPORTAL) != 0
}
/// monster.conf `mob_warp: 0`.
pub fn mob_warp() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_WARP)
}
/// skill.conf `gx_allhit: no`.
pub fn gx_allhit() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::GX_ALLHIT) != 0
}
/// skill.conf `multi_trigger_trap: no`.
pub fn multi_trigger_trap() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MULTI_TRIGGER_TRAP)
}
/// skill.conf `refresh_song: no`, `refresh_song_icon: no` (battle_config.refresh_song / refresh_song_icon).
pub fn refresh_song() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::REFRESH_SONG)
}
pub fn refresh_song_icon() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::REFRESH_SONG_ICON)
}
/// skill.conf `player_skill_partner_check: yes`.
pub fn player_skill_partner_check() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PLAYER_SKILL_PARTNER_CHECK) != 0
}
/// skill.conf `can_damage_skill: 1` (BL_ mask: BL_PC).
pub fn can_damage_skill() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::CAN_DAMAGE_SKILL) as u16
}
/// skill.conf `clear_skills_on_warp: 15` (battle_config.clear_unit_onwarp, BL_ mask) and `clear_skills_on_death: 0`
/// (battle_config.clear_unit_ondeath).
pub fn clear_unit_onwarp() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::CLEAR_SKILLS_ON_WARP) as u16
}
pub fn clear_unit_ondeath() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::CLEAR_SKILLS_ON_DEATH) as u16
}
/// misc.conf `skill_log: off` (BL_ mask).
pub fn skill_log() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_LOG) as u16
}
/// monster.conf `mob_icewall_walk_block: 75`, `boss_icewall_walk_block: 0`.
pub fn mob_icewall_walk_block() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_ICEWALL_WALK_BLOCK)
}
pub fn boss_icewall_walk_block() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::BOSS_ICEWALL_WALK_BLOCK)
}
/// skill.conf `skip_teleport_lv1_menu: no`, misc.conf `duel_allow_teleport: no`.
pub fn skip_teleport_lv1_menu() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SKIP_TELEPORT_LV1_MENU) != 0
}
pub fn duel_allow_teleport() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_ALLOW_TELEPORT) != 0
}

/// (battle_data key, value) of every constant above, for the check against script/battle_flags.rs.
pub fn checked() -> Vec<(&'static str, i32)> {
    vec![
        ("traps_setting", traps_setting()),
        ("gvg_traps_target_all", vs_traps_bctall() as i32),
        ("skill_wall_check", skill_wall_check() as i32),
        ("land_protector_behavior", land_protector_behavior() as i32),
        ("skill_reiteration", skill_reiteration() as i32),
        ("skill_nofootset", skill_nofootset() as i32),
        ("land_skill_limit", land_skill_limit() as i32),
        ("chat_warpportal", chat_warpportal() as i32),
        ("mob_warp", mob_warp()),
        ("gx_allhit", gx_allhit() as i32),
        ("multi_trigger_trap", multi_trigger_trap()),
        ("refresh_song", refresh_song()),
        ("refresh_song_icon", refresh_song_icon()),
        ("player_skill_partner_check", player_skill_partner_check() as i32),
        ("can_damage_skill", can_damage_skill() as i32),
        ("clear_skills_on_warp", clear_unit_onwarp() as i32),
        ("clear_skills_on_death", clear_unit_ondeath() as i32),
        ("skill_log", skill_log() as i32),
        ("mob_icewall_walk_block", mob_icewall_walk_block()),
        ("boss_icewall_walk_block", boss_icewall_walk_block()),
        ("skip_teleport_lv1_menu", skip_teleport_lv1_menu() as i32),
        ("duel_allow_teleport", duel_allow_teleport() as i32),
    ]
}
