//! The battle_config values battle.cpp / unit.cpp's attack code / pc_dead read (conf/battle/*.conf of rAthena
//! e985006 after battle_adjust_conf, as the reference map-server runs them; `script/battle_flags.rs` holds the whole
//! table and a unit test below checks every value here against it). Each constant names its file and key.

// ---------------------------------------------------------------------------------------------- battle.conf
/// battle.conf `delay_battle_damage: yes`.
pub fn delay_battle_damage() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DELAY_BATTLE_DAMAGE) != 0
}
/// battle.conf `synchronize_damage: no`.
pub fn synchronize_damage() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SYNCHRONIZE_DAMAGE) != 0
}
/// battle.conf `snap_dodge: no`.
pub fn snap_dodge() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SNAP_DODGE) != 0
}
/// battle.conf `attack_walk_delay: 0` (BL_ types).
pub fn attack_walk_delay() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::ATTACK_WALK_DELAY) as u16
}
/// battle.conf `damage_walk_delay: 0` (BL_ types).
pub fn damage_walk_delay() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::DAMAGE_WALK_DELAY) as u16
}
/// battle.conf `multihit_delay: 200`.
pub fn multihit_delay() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MULTIHIT_DELAY) as i64
}
/// battle.conf `pc_damage_walk_delay_rate: 20` (battle_config.pc_walk_delay_rate).
pub fn pc_walk_delay_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PC_DAMAGE_WALK_DELAY_RATE)
}
/// battle.conf `damage_walk_delay_rate: 100` (battle_config.walk_delay_rate).
pub fn walk_delay_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DAMAGE_WALK_DELAY_RATE)
}
/// battle.conf `agi_penalty_target: 1`, `agi_penalty_type: 1`, `agi_penalty_count: 3`, `agi_penalty_num: 10`.
pub fn agi_penalty_target() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::AGI_PENALTY_TARGET) as u16
}
pub fn agi_penalty_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::AGI_PENALTY_TYPE)
}
pub fn agi_penalty_count() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::AGI_PENALTY_COUNT)
}
pub fn agi_penalty_num() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::AGI_PENALTY_NUM)
}
/// battle.conf `vit_penalty_target: 1`, `vit_penalty_type: 1`, `vit_penalty_count: 3`, `vit_penalty_num: 5`.
pub fn vit_penalty_target() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::VIT_PENALTY_TARGET) as u16
}
pub fn vit_penalty_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::VIT_PENALTY_TYPE)
}
pub fn vit_penalty_count() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::VIT_PENALTY_COUNT)
}
pub fn vit_penalty_num() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::VIT_PENALTY_NUM)
}
/// battle.conf `weapon_defense_type: 0`.
pub fn weapon_defense_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::WEAPON_DEFENSE_TYPE)
}
/// battle.conf `min_hitrate: 5`, `max_hitrate: 100`.
pub fn min_hitrate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MIN_HITRATE)
}
pub fn max_hitrate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAX_HITRATE)
}
/// battle.conf `attribute_recover: no` (battle_config.attr_recover).
pub fn attr_recover() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ATTRIBUTE_RECOVER) != 0
}
/// battle.conf `attack_attr_none: 14` (~BL_PC: non-player normal attacks are non-elemental).
pub fn attack_attr_none() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::ATTACK_ATTR_NONE).clamp(0, 65535) as u16
}
/// battle.conf `skill_min_damage: 0`.
pub fn skill_min_damage() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_MIN_DAMAGE)
}
/// battle.conf `max_res_mres_ignored: 50`.
pub fn max_res_mres_ignored() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAX_RES_MRES_IGNORED)
}
/// battle.conf `enable_critical: 17` (BL_PC|BL_MER: homunculi do not crit), `critical_rate: 100`, `mob_critical_rate: 100`.
pub fn enable_critical() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::ENABLE_CRITICAL) as u16
}
/// battle.conf `auto_counter_type: 15`.
pub fn auto_counter_type() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::AUTO_COUNTER_TYPE) as u16
}
/// battle.conf `left_cardfix_to_right: yes`, `cardfix_monster_physical: yes` (the left value is conf.rs's).
pub fn cardfix_monster_physical() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::CARDFIX_MONSTER_PHYSICAL) != 0
}
/// battle.conf `skillrange_by_distance: 14` (~BL_PC).
pub fn skillrange_by_distance() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::SKILLRANGE_BY_DISTANCE).clamp(0, 65535) as u16
}
/// battle.conf `attack_direction_change: 0`.
pub fn attack_direction_change() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::ATTACK_DIRECTION_CHANGE) as u16
}
/// battle.conf `devotion_rdamage: 0`, `devotion_rdamage_skill_only: yes`, `devotion_standup_fix: yes`.
pub fn devotion_rdamage() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DEVOTION_RDAMAGE)
}
pub fn devotion_rdamage_skill_only() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DEVOTION_RDAMAGE_SKILL_ONLY) != 0
}
pub fn devotion_standup_fix() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DEVOTION_STANDUP_FIX) != 0
}
/// battle.conf `gvg_short_attack_damage_rate: 80`, `gvg_long_attack_damage_rate: 80`,
/// `gvg_weapon_attack_damage_rate: 60`, `gvg_magic_attack_damage_rate: 60`, `gvg_misc_attack_damage_rate: 60`
/// (guild.conf).
pub fn gvg_short_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::GVG_SHORT_ATTACK_DAMAGE_RATE) as i64
}
pub fn gvg_long_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::GVG_LONG_ATTACK_DAMAGE_RATE) as i64
}
pub fn gvg_weapon_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::GVG_WEAPON_ATTACK_DAMAGE_RATE) as i64
}
pub fn gvg_magic_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::GVG_MAGIC_ATTACK_DAMAGE_RATE) as i64
}
pub fn gvg_misc_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::GVG_MISC_ATTACK_DAMAGE_RATE) as i64
}
/// battleground.conf `bg_short_attack_damage_rate: 80` … `bg_misc_attack_damage_rate: 60`.
pub fn bg_short_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::BG_SHORT_ATTACK_DAMAGE_RATE) as i64
}
pub fn bg_long_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::BG_LONG_ATTACK_DAMAGE_RATE) as i64
}
pub fn bg_weapon_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::BG_WEAPON_ATTACK_DAMAGE_RATE) as i64
}
pub fn bg_magic_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::BG_MAGIC_ATTACK_DAMAGE_RATE) as i64
}
pub fn bg_misc_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::BG_MISC_ATTACK_DAMAGE_RATE) as i64
}
/// misc.conf `pk_short_attack_damage_rate: 80`, `pk_long_attack_damage_rate: 70`, `pk_weapon_attack_damage_rate: 60`,
/// `pk_magic_attack_damage_rate: 60`, `pk_misc_attack_damage_rate: 60`, `pk_min_level: 55`, `pk_level_range: 0`.
pub fn pk_short_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::PK_SHORT_ATTACK_DAMAGE_RATE) as i64
}
pub fn pk_long_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::PK_LONG_ATTACK_DAMAGE_RATE) as i64
}
pub fn pk_weapon_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::PK_WEAPON_ATTACK_DAMAGE_RATE) as i64
}
pub fn pk_magic_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::PK_MAGIC_ATTACK_DAMAGE_RATE) as i64
}
pub fn pk_misc_damage_rate() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::PK_MISC_ATTACK_DAMAGE_RATE) as i64
}
pub fn pk_min_level() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PK_MIN_LEVEL)
}
pub fn pk_level_range() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PK_LEVEL_RANGE)
}
/// battle.conf `hide_woe_damage: no`.
pub fn hide_woe_damage() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::HIDE_WOE_DAMAGE) != 0
}
/// battle.conf `show_hp_sp_drain: no`.
pub fn show_hp_sp_drain() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SHOW_HP_SP_DRAIN) != 0
}
/// monster.conf `ksprotection: 0`.
pub fn ksprotection() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::KSPROTECTION)
}
/// monster.conf `monster_ai: 0` (battle_config.mob_ai).
pub fn mob_ai() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_AI)
}
/// monster.conf `mobs_level_up: no`.
pub fn mobs_level_up() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MOBS_LEVEL_UP) != 0
}
/// skill.conf `skill_delay_attack_enable: yes` (battle_config.sdelay_attack_enable).
pub fn sdelay_attack_enable() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_DELAY_ATTACK_ENABLE) != 0
}
/// skill.conf `path_blown_halt: yes`, `skill_trap_type: 0`.
pub fn path_blown_halt() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PATH_BLOWN_HALT) != 0
}
pub fn skill_trap_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_TRAP_TYPE)
}
/// skill.conf `alchemist_summon_setting: 12`.
pub fn alchemist_summon_setting() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::ALCHEMIST_SUMMON_SETTING)
}
/// items.conf `arrow_decrement: 1`.
pub fn arrow_decrement() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::ARROW_DECREMENT)
}
/// misc.conf `duel_allow_pvp: no`, `duel_allow_gvg: no`, `duel_autoleave_when_die: yes`.
pub fn duel_allow_pvp() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_ALLOW_PVP) != 0
}
pub fn duel_allow_gvg() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_ALLOW_GVG) != 0
}
pub fn duel_autoleave_when_die() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_AUTOLEAVE_WHEN_DIE) != 0
}
/// pet.conf `pet_attack_support: no`, `pet_damage_support: no`.
pub fn pet_attack_support() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_ATTACK_SUPPORT) != 0
}
pub fn pet_damage_support() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_DAMAGE_SUPPORT) != 0
}
/// exp.conf `death_penalty_type: 1`, `death_penalty_base: 100`, `death_penalty_job: 100`, `zeny_penalty: 0`,
/// `death_penalty_maxlv: 0`.
pub fn death_penalty_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DEATH_PENALTY_TYPE)
}
pub fn death_penalty_base() -> u64 {
    crate::battle_config::get(crate::battle_config::Bc::DEATH_PENALTY_BASE) as u64
}
pub fn death_penalty_job() -> u64 {
    crate::battle_config::get(crate::battle_config::Bc::DEATH_PENALTY_JOB) as u64
}
pub fn zeny_penalty() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::ZENY_PENALTY) as u32
}
pub fn death_penalty_maxlv() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DEATH_PENALTY_MAXLV)
}
/// player.conf `loose_ap_on_death: yes`.
pub fn loose_ap_on_death() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::LOOSE_AP_ON_DEATH) != 0
}
/// misc.conf `bone_drop: 0`.
pub fn bone_drop() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::BONE_DROP)
}
/// homunc.conf `homunculus_auto_vapor: 80`.
pub fn homunculus_auto_vapor() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_AUTO_VAPOR)
}
/// skill.conf `min_skill_delay_limit: 100`.
pub fn min_skill_delay_limit() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MIN_SKILL_DELAY_LIMIT) as i64
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::script::battle_flags::battle_get_value as v;

    /// Every constant here equals what the reference map-server runs with (script/battle_flags.rs).
    #[test]
    fn battle_config_values_match_the_reference_server() {
        let pairs: &[(&str, i64)] = &[
            ("delay_battle_damage", delay_battle_damage() as i64),
            ("synchronize_damage", synchronize_damage() as i64),
            ("snap_dodge", snap_dodge() as i64),
            ("attack_walk_delay", attack_walk_delay() as i64),
            ("damage_walk_delay", damage_walk_delay() as i64),
            ("multihit_delay", multihit_delay()),
            ("pc_damage_walk_delay_rate", pc_walk_delay_rate() as i64),
            ("damage_walk_delay_rate", walk_delay_rate() as i64),
            ("agi_penalty_target", agi_penalty_target() as i64),
            ("agi_penalty_type", agi_penalty_type() as i64),
            ("agi_penalty_count", agi_penalty_count() as i64),
            ("agi_penalty_num", agi_penalty_num() as i64),
            ("vit_penalty_target", vit_penalty_target() as i64),
            ("vit_penalty_type", vit_penalty_type() as i64),
            ("vit_penalty_count", vit_penalty_count() as i64),
            ("vit_penalty_num", vit_penalty_num() as i64),
            ("weapon_defense_type", weapon_defense_type() as i64),
            ("min_hitrate", min_hitrate() as i64),
            ("max_hitrate", max_hitrate() as i64),
            ("attribute_recover", attr_recover() as i64),
            ("attack_attr_none", attack_attr_none() as i64),
            ("skill_min_damage", skill_min_damage() as i64),
            ("max_res_mres_ignored", max_res_mres_ignored() as i64),
            ("enable_critical", enable_critical() as i64),
            ("auto_counter_type", auto_counter_type() as i64),
            ("left_cardfix_to_right", crate::conf::left_cardfix_to_right() as i64),
            ("cardfix_monster_physical", cardfix_monster_physical() as i64),
            ("skillrange_by_distance", skillrange_by_distance() as i64),
            ("attack_direction_change", attack_direction_change() as i64),
            ("devotion_rdamage", devotion_rdamage() as i64),
            ("devotion_rdamage_skill_only", devotion_rdamage_skill_only() as i64),
            ("devotion_standup_fix", devotion_standup_fix() as i64),
            ("gvg_short_attack_damage_rate", gvg_short_damage_rate()),
            ("gvg_long_attack_damage_rate", gvg_long_damage_rate()),
            ("gvg_weapon_attack_damage_rate", gvg_weapon_damage_rate()),
            ("gvg_magic_attack_damage_rate", gvg_magic_damage_rate()),
            ("gvg_misc_attack_damage_rate", gvg_misc_damage_rate()),
            ("bg_short_attack_damage_rate", bg_short_damage_rate()),
            ("bg_long_attack_damage_rate", bg_long_damage_rate()),
            ("bg_weapon_attack_damage_rate", bg_weapon_damage_rate()),
            ("bg_magic_attack_damage_rate", bg_magic_damage_rate()),
            ("bg_misc_attack_damage_rate", bg_misc_damage_rate()),
            ("pk_short_attack_damage_rate", pk_short_damage_rate()),
            ("pk_long_attack_damage_rate", pk_long_damage_rate()),
            ("pk_weapon_attack_damage_rate", pk_weapon_damage_rate()),
            ("pk_magic_attack_damage_rate", pk_magic_damage_rate()),
            ("pk_misc_attack_damage_rate", pk_misc_damage_rate()),
            ("pk_min_level", pk_min_level() as i64),
            ("pk_level_range", pk_level_range() as i64),
            ("pk_mode", crate::conf::pk_mode() as i64),
            ("hide_woe_damage", hide_woe_damage() as i64),
            ("show_hp_sp_drain", show_hp_sp_drain() as i64),
            ("ksprotection", ksprotection() as i64),
            ("monster_ai", mob_ai() as i64),
            ("mobs_level_up", mobs_level_up() as i64),
            ("skill_delay_attack_enable", sdelay_attack_enable() as i64),
            ("path_blown_halt", path_blown_halt() as i64),
            ("skill_trap_type", skill_trap_type() as i64),
            ("alchemist_summon_setting", alchemist_summon_setting() as i64),
            ("arrow_decrement", arrow_decrement() as i64),
            ("duel_allow_pvp", duel_allow_pvp() as i64),
            ("duel_allow_gvg", duel_allow_gvg() as i64),
            ("duel_autoleave_when_die", duel_autoleave_when_die() as i64),
            ("pet_attack_support", pet_attack_support() as i64),
            ("pet_damage_support", pet_damage_support() as i64),
            ("death_penalty_type", death_penalty_type() as i64),
            ("death_penalty_base", death_penalty_base() as i64),
            ("death_penalty_job", death_penalty_job() as i64),
            ("zeny_penalty", zeny_penalty() as i64),
            ("death_penalty_maxlv", death_penalty_maxlv() as i64),
            ("loose_ap_on_death", loose_ap_on_death() as i64),
            ("bone_drop", bone_drop() as i64),
            ("homunculus_auto_vapor", homunculus_auto_vapor() as i64),
            ("min_skill_delay_limit", min_skill_delay_limit()),
            ("prevent_logout", crate::conf::prevent_logout()),
            ("prevent_logout_trigger", crate::conf::prevent_logout_trigger() as i64),
            ("idletime_option", crate::conf::idletime_option() as i64),
            ("player_damage_delay_rate", crate::conf::pc_damage_delay_rate() as i64),
            ("undead_detect_type", crate::conf::undead_detect_type() as i64),
        ];
        for &(k, want) in pairs {
            assert_eq!(v(k) as i64, want, "battle_config {k}");
        }
    }
}
