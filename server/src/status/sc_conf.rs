//! The battle_config values the status change code reads (conf/battle/*.conf of rAthena e985006; Loki's import
//! `server/data/overlay/conf/import/battle_conf.txt` changes none of them). Each names its file and key.

/// skill.conf `player_cloak_check_type: 1` (1: check walls, 2: no end on normal attacks, 4: no end on skills).
pub fn pc_cloak_check_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PLAYER_CLOAK_CHECK_TYPE)
}
/// skill.conf `monster_cloak_check_type: 4`.
pub fn monster_cloak_check_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_CLOAK_CHECK_TYPE)
}
/// skill.conf `combo_delay_rate: 100`.
pub fn combo_delay_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::COMBO_DELAY_RATE)
}
/// player.conf `max_parameter: 99`.
pub fn max_parameter() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAX_PARAMETER)
}
/// client.conf `max_cloth_color: 7`.
pub fn max_cloth_color() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAX_CLOTH_COLOR)
}
