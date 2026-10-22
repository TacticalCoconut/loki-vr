//! The battle_config values the skill pipeline reads (conf/battle/*.conf of rAthena e985006 after
//! battle_adjust_conf, as the reference map-server runs them). Each constant names its conf file / battle.cpp key; a
//! unit test checks every value against `script/battle_flags.rs` (the reference server's whole table).

/// skill.conf `skill_wall_check: yes`.
pub fn skill_wall_check() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_WALL_CHECK) != 0
}
/// client.conf `show_skill_scale: yes` (PACKETVER >= 20151223).
pub fn show_skill_scale() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SHOW_SKILL_SCALE) != 0
}
/// skill.conf `display_skill_fail: 2` (&1 no messages, &2 no SKILLINTERVAL without @showdelay, &4 Snatcher, &8 Envenom).
pub fn display_skill_fail() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DISPLAY_SKILL_FAIL)
}
/// skill.conf `skill_add_range: 0`, `skill_out_range_consume: no`.
pub fn skill_add_range() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_ADD_RANGE)
}
pub fn skill_out_range_consume() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_OUT_RANGE_CONSUME) != 0
}
/// skill.conf `min_skill_delay_limit: 100`, `default_walk_delay: 300`, `no_skill_delay: 2` (BL_MOB).
pub fn min_skill_delay_limit() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MIN_SKILL_DELAY_LIMIT) as i64
}
pub fn default_walk_delay() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::DEFAULT_WALK_DELAY) as i64
}
pub fn no_skill_delay() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::NO_SKILL_DELAY) as u16
}
/// skill.conf `casting_rate: 100`, `delay_rate: 100`, `delay_dependon_dex: no`, `delay_dependon_agi: no`.
pub fn casting_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::CASTING_RATE)
}
pub fn delay_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DELAY_RATE)
}
pub fn delay_dependon_dex() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DELAY_DEPENDON_DEX) != 0
}
pub fn delay_dependon_agi() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DELAY_DEPENDON_AGI) != 0
}
/// skill.conf `castrate_dex_scale: 150`, `vcast_stat_scale: 530`, `default_fixed_castrate: 20`.
pub fn castrate_dex_scale() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::CASTRATE_DEX_SCALE)
}
pub fn vcast_stat_scale() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::VCAST_STAT_SCALE)
}
pub fn default_fixed_castrate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DEFAULT_FIXED_CASTRATE)
}
/// skill.conf `skill_amotion_leniency: 0`.
pub fn skill_amotion_leniency() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_AMOTION_LENIENCY)
}
/// misc.conf `prevent_logout_trigger: 14` (PLT_LOGIN 0x1 | PLT_ATTACK 0x2 | PLT_SKILL 0x4 | PLT_DAMAGE 0x8).
pub fn prevent_logout_trigger() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PREVENT_LOGOUT_TRIGGER)
}
pub const PLT_SKILL: i32 = 0x4;
/// misc.conf `ksprotection: 0`.
pub fn ksprotection() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::KSPROTECTION)
}
/// skill.conf `skill_caster_check: yes`, `skill_log: no` (0).
pub fn skill_caster_check() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_CASTER_CHECK) != 0
}
pub fn skill_log() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_LOG) as u16
}
/// skill.conf `skill_reiteration: 0`, `skill_nofootset: 1` (BL_PC), `land_skill_limit: 9` (BL_PC|BL_HOM).
pub fn skill_reiteration() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_REITERATION) as u16
}
pub fn skill_nofootset() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_NOFOOTSET) as u16
}
pub fn land_skill_limit() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::LAND_SKILL_LIMIT) as u16
}
/// skill.conf `combo_delay_rate: 100`.
pub fn combo_delay_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::COMBO_DELAY_RATE)
}
/// skill.conf `autospell_check_range: no`.
pub fn autospell_check_range() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::AUTOSPELL_CHECK_RANGE) != 0
}
/// skill.conf `gx_disptype: 1`, `eq_single_target_reflectable: yes`.
pub fn gx_disptype() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::GX_DISPTYPE) != 0
}
pub fn eq_single_target_reflectable() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::EQ_SINGLE_TARGET_REFLECTABLE) != 0
}
/// skill.conf `skill_eightpath_algorithm: yes`, `skill_eightpath_same_cell: yes`.
pub fn skill_eightpath_algorithm() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_EIGHTPATH_ALGORITHM) != 0
}
pub fn skill_eightpath_same_cell() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_EIGHTPATH_SAME_CELL) != 0
}
/// skill.conf `stormgust_knockback: yes`, `cart_revo_knockback: yes`, `arrow_shower_knockback: yes`.
pub fn stormgust_knockback() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::STORMGUST_KNOCKBACK) != 0
}
pub fn cart_revo_knockback() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::CART_REVO_KNOCKBACK) != 0
}
pub fn arrow_shower_knockback() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ARROW_SHOWER_KNOCKBACK) != 0
}
/// skill.conf `max_heal_lv: 11`, `max_heal: 9999`.
pub fn max_heal_lv() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::MAX_HEAL_LV).clamp(0, 65535) as u16
}
pub fn max_heal() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MAX_HEAL)
}
/// battle.conf `skillrange_from_weapon: 0` (battle_config.use_weapon_skill_range, BL_ types).
pub fn use_weapon_skill_range() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::SKILLRANGE_FROM_WEAPON) as u16
}
/// monster.conf `monster_ai: 0` (battle_config.mob_ai), `monster_eye_range_bonus: 0`.
pub fn mob_ai() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_AI)
}
pub fn mob_eye_range_bonus() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_EYE_RANGE_BONUS)
}
/// skill.conf `amotion_min_skill_delay: no`.
pub fn amotion_min_skill_delay() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::AMOTION_MIN_SKILL_DELAY) != 0
}
/// skill.conf `status_cast_cancel: 0` (battle_config.sc_castcancel, BL_ types).
pub fn sc_castcancel() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::STATUS_CAST_CANCEL) as u16
}
/// guild.conf `emergency_call: 11`.
pub fn emergency_call() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::EMERGENCY_CALL)
}
/// skill.conf `traps_setting: 2`, `skill_trap_type: 0`.
pub fn traps_setting() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::TRAPS_SETTING)
}
/// battle.conf `summons_trigger_autospells: yes`.
pub fn summons_trigger_autospells() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SUMMONS_TRIGGER_AUTOSPELLS) != 0
}
/// battle.conf `equip_natural_break_rate: 0`, `equip_self_break_rate: 100`, `equip_skill_break_rate: 100`,
/// `break_mob_equip: no`.
pub fn equip_natural_break_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::EQUIP_NATURAL_BREAK_RATE)
}
pub fn equip_self_break_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::EQUIP_SELF_BREAK_RATE)
}
pub fn equip_skill_break_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::EQUIP_SKILL_BREAK_RATE)
}
pub fn break_mob_equip() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::BREAK_MOB_EQUIP) != 0
}
/// battle.conf `left_cardfix_to_right: yes`.
pub fn left_cardfix_to_right() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::LEFT_CARDFIX_TO_RIGHT) != 0
}
/// monster.conf `exp_bonus_nodamage_attacker: 0`... (battle.cpp key `exp_bonus_nodamage_attacker`).
pub fn exp_bonus_nodamage_attacker() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::EXP_BONUS_NODAMAGE_ATTACKER)
}
/// skill.conf `player_skill_partner_check: yes`.
pub fn player_skill_partner_check() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PLAYER_SKILL_PARTNER_CHECK) != 0
}
/// monster.conf `mob_skill_rate: 100`, `mob_skill_delay: 100`, `mob_max_skilllvl: 100`,
/// `mob_changetarget_byskill: no`, `mob_nopc_idleskill_rate: 100`, `boss_nopc_idleskill_rate: 100`.
pub fn mob_skill_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_SKILL_RATE)
}
pub fn mob_skill_delay() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_SKILL_DELAY)
}
pub fn mob_max_skilllvl() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_MAX_SKILLLVL)
}
pub fn mob_changetarget_byskill() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MOB_CHANGETARGET_BYSKILL) != 0
}
pub fn mob_nopc_idleskill_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_NOPC_IDLESKILL_RATE)
}
pub fn boss_nopc_idleskill_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::BOSS_NOPC_IDLESKILL_RATE)
}
/// skill.conf `can_damage_skill: 1` (BL_PC).
pub fn can_damage_skill() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::CAN_DAMAGE_SKILL) as u16
}
/// battle.conf `sg_miracle_skill_ratio: 1`, `sg_miracle_skill_duration: 3600000`.
pub fn sg_miracle_skill_ratio() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SG_MIRACLE_SKILL_RATIO)
}
pub fn sg_miracle_skill_duration() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::SG_MIRACLE_SKILL_DURATION) as i64
}
/// battle.conf `show_hp_sp_gain: yes`.
pub fn show_hp_sp_gain() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SHOW_HP_SP_GAIN) != 0
}
/// skill.conf `skill_add_heal_rate: 487`.
pub fn skill_add_heal_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_ADD_HEAL_RATE)
}
/// skill.conf `player_cloak_check_type: 1` (battle.cpp key `player_cloak_check_type`, battle_config.pc_cloak_check_type),
/// `monster_cloak_check_type: 4`.
pub fn pc_cloak_check_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PLAYER_CLOAK_CHECK_TYPE)
}
pub fn monster_cloak_check_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_CLOAK_CHECK_TYPE)
}
/// skill.conf `switch_remove_edp: 2`.
pub fn switch_remove_edp() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SWITCH_REMOVE_EDP)
}
/// misc.conf `duel_allow_teleport: no`.
pub fn duel_allow_teleport() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DUEL_ALLOW_TELEPORT) != 0
}
/// skill.conf `sense_type: 1` (battle.cpp key `sense_type`, battle_config.estimation_type): &1 base DEF / MDEF,
/// &2 VIT / INT DEF in Sense's window (clif_skill_estimation).
pub fn estimation_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SENSE_TYPE)
}
/// battle.conf `warg_can_falcon: no`.
pub fn warg_can_falcon() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::WARG_CAN_FALCON) != 0
}

#[cfg(test)]
mod tests {
    use super::*;

    fn flag(name: &str) -> i64 {
        crate::script::battle_flags::BATTLE_FLAGS.iter().find(|(n, _)| *n == name).map(|(_, v)| *v as i64).unwrap_or_else(|| panic!("no battle flag {name}"))
    }

    #[test]
    fn values_match_the_reference_server() {
        let checks: &[(&str, i64)] = &[
            ("skill_wall_check", skill_wall_check() as i64),
            ("show_skill_scale", show_skill_scale() as i64),
            ("display_skill_fail", display_skill_fail() as i64),
            ("skill_add_range", skill_add_range() as i64),
            ("skill_out_range_consume", skill_out_range_consume() as i64),
            ("min_skill_delay_limit", min_skill_delay_limit()),
            ("default_walk_delay", default_walk_delay()),
            ("no_skill_delay", no_skill_delay() as i64),
            ("casting_rate", casting_rate() as i64),
            ("delay_rate", delay_rate() as i64),
            ("delay_dependon_dex", delay_dependon_dex() as i64),
            ("delay_dependon_agi", delay_dependon_agi() as i64),
            ("castrate_dex_scale", castrate_dex_scale() as i64),
            ("vcast_stat_scale", vcast_stat_scale() as i64),
            ("default_fixed_castrate", default_fixed_castrate() as i64),
            ("skill_amotion_leniency", skill_amotion_leniency() as i64),
            ("prevent_logout_trigger", prevent_logout_trigger() as i64),
            ("ksprotection", ksprotection() as i64),
            ("skill_caster_check", skill_caster_check() as i64),
            ("skill_log", skill_log() as i64),
            ("skill_reiteration", skill_reiteration() as i64),
            ("skill_nofootset", skill_nofootset() as i64),
            ("land_skill_limit", land_skill_limit() as i64),
            ("combo_delay_rate", combo_delay_rate() as i64),
            ("autospell_check_range", autospell_check_range() as i64),
            ("gx_disptype", gx_disptype() as i64),
            ("eq_single_target_reflectable", eq_single_target_reflectable() as i64),
            ("skill_eightpath_algorithm", skill_eightpath_algorithm() as i64),
            ("skill_eightpath_same_cell", skill_eightpath_same_cell() as i64),
            ("stormgust_knockback", stormgust_knockback() as i64),
            ("cart_revo_knockback", cart_revo_knockback() as i64),
            ("arrow_shower_knockback", arrow_shower_knockback() as i64),
            ("max_heal_lv", max_heal_lv() as i64),
            ("max_heal", max_heal() as i64),
            ("skillrange_from_weapon", use_weapon_skill_range() as i64),
            ("monster_ai", mob_ai() as i64),
            ("monster_eye_range_bonus", mob_eye_range_bonus() as i64),
            ("amotion_min_skill_delay", amotion_min_skill_delay() as i64),
            ("status_cast_cancel", sc_castcancel() as i64),
            ("emergency_call", emergency_call() as i64),
            ("traps_setting", traps_setting() as i64),
            ("summons_trigger_autospells", summons_trigger_autospells() as i64),
            ("equip_natural_break_rate", equip_natural_break_rate() as i64),
            ("equip_self_break_rate", equip_self_break_rate() as i64),
            ("equip_skill_break_rate", equip_skill_break_rate() as i64),
            ("break_mob_equip", break_mob_equip() as i64),
            ("left_cardfix_to_right", left_cardfix_to_right() as i64),
            ("exp_bonus_nodamage_attacker", exp_bonus_nodamage_attacker() as i64),
            ("player_skill_partner_check", player_skill_partner_check() as i64),
            ("mob_skill_rate", mob_skill_rate() as i64),
            ("mob_skill_delay", mob_skill_delay() as i64),
            ("mob_max_skilllvl", mob_max_skilllvl() as i64),
            ("mob_changetarget_byskill", mob_changetarget_byskill() as i64),
            ("mob_nopc_idleskill_rate", mob_nopc_idleskill_rate() as i64),
            ("boss_nopc_idleskill_rate", boss_nopc_idleskill_rate() as i64),
            ("can_damage_skill", can_damage_skill() as i64),
            ("sg_miracle_skill_ratio", sg_miracle_skill_ratio() as i64),
            ("sg_miracle_skill_duration", sg_miracle_skill_duration()),
            ("skill_add_heal_rate", skill_add_heal_rate() as i64),
            ("show_hp_sp_gain", show_hp_sp_gain() as i64),
            ("player_cloak_check_type", pc_cloak_check_type() as i64),
            ("monster_cloak_check_type", monster_cloak_check_type() as i64),
            ("switch_remove_edp", switch_remove_edp() as i64),
            ("duel_allow_teleport", duel_allow_teleport() as i64),
            ("sense_type", estimation_type() as i64),
            ("warg_can_falcon", warg_can_falcon() as i64),
        ];
        for (name, v) in checks {
            assert_eq!(flag(name), *v, "battle_config {name}");
        }
    }
}
