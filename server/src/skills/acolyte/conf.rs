//! The battle_config values the acolyte family's classes read (rAthena conf/battle/*.conf; no Loki override in
//! server/data/overlay/conf/import/battle_conf.txt names them). script/battle_flags.rs lists the same values for the
//! getbattleflag script command.

/// conf/battle/exp.conf `heal_exp: 0` (battle_config.heal_exp: job exp for healing others, in % of the heal).
pub fn heal_exp() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::HEAL_EXP)
}

/// conf/battle/exp.conf `resurrection_exp: 0` (battle_config.resurrection_exp: exp for resurrecting a higher level
/// player, in 1/10000 % of the target's exp per level of difference).
pub fn resurrection_exp() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::RESURRECTION_EXP)
}

/// conf/battle/skill.conf `finger_offensive_type: 0` (battle_config.finger_offensive_type: 0 = one hit of all the
/// spheres, 1 = one hit per sphere, 200 ms apart).
pub fn finger_offensive_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FINGER_OFFENSIVE_TYPE)
}

/// conf/battle/skill.conf `dispel_song: no` (battle_config.dispel_song: "Should Dispel work on songs when the target
/// is not in the song area?": Clearance also ends the song statuses of targets outside the song's area, val4 != 0).
pub fn dispel_song() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DISPEL_SONG) != 0
}
