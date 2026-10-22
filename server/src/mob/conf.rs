//! The battle_config values the monster code reads (conf/battle/*.conf of rAthena e985006; Loki's
//! conf/import/battle_conf.txt changes none of them). Values the database loader already applied (mvp_hp_rate,
//! monster_hp_rate, view/chase_range_rate, monster_active_enable, monster_damage_delay_rate, mob_skill_rate /
//! mob_skill_delay, the item_rate_* drop rates, base/job/mvp_exp_rate) live in `db/mobs.rs` data, not here.
//! The general ones shared with other systems (dynamic_mobs, mob_respawn_time, mob_spawn_variance,
//! randomize_center_cell, mob_warp, monster_hp_bars_info, area_size, official_cell_stack_limit) are in `crate::conf`.

/// monster.conf `monster_ai: 0` (battle_config.mob_ai bit field).
pub fn mob_ai() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_AI)
}
/// monster.conf `monster_chase_refresh: 32` (battle_config.mob_chase_refresh).
pub fn mob_chase_refresh() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_CHASE_REFRESH)
}
/// monster.conf `mob_active_time: 0`, `boss_active_time: 0`.
pub fn mob_active_time() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_ACTIVE_TIME) as i64
}
pub fn boss_active_time() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::BOSS_ACTIVE_TIME) as i64
}
/// monster.conf `loot_range: 12`, `assist_range: 11`.
pub fn loot_range() -> i16 {
    crate::battle_config::get(crate::battle_config::Bc::LOOT_RANGE) as i16
}
pub fn assist_range() -> i16 {
    crate::battle_config::get(crate::battle_config::Bc::ASSIST_RANGE) as i16
}
/// monster.conf `monster_loot_type: 0`, `monster_loot_search_type: 1`.
pub fn monster_loot_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_LOOT_TYPE)
}
pub fn monster_loot_search_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_LOOT_SEARCH_TYPE)
}
/// monster.conf `mob_skill_rate: 100` (mobskill_use returns at once when 0; a clone's skill chances).
pub fn mob_skill_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_SKILL_RATE)
}
/// monster.conf `mob_skill_delay: 100` (a clone's skill delays; the database's are applied at load).
pub fn mob_skill_delay() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_SKILL_DELAY)
}
/// monster.conf `mob_spawn_delay: 100`, `plant_spawn_delay: 100`, `boss_spawn_delay: 100`.
pub fn mob_spawn_delay() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_SPAWN_DELAY) as u32
}
pub fn plant_spawn_delay() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::PLANT_SPAWN_DELAY) as u32
}
pub fn boss_spawn_delay() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::BOSS_SPAWN_DELAY) as u32
}
/// monster.conf `no_spawn_on_player: 0`.
pub fn no_spawn_on_player() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::NO_SPAWN_ON_PLAYER)
}
/// monster.conf `slaves_inherit_mode: 4`, `slaves_inherit_speed: 3`.
pub fn slaves_inherit_mode() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SLAVES_INHERIT_MODE)
}
pub fn slaves_inherit_speed() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::SLAVES_INHERIT_SPEED)
}
/// monster.conf `mob_slave_keep_target: yes`, `slave_stick_with_master: no`, `slave_active_with_master: no`.
pub fn mob_slave_keep_target() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MOB_SLAVE_KEEP_TARGET) != 0
}
pub fn slave_stick_with_master() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SLAVE_STICK_WITH_MASTER) != 0
}
pub fn slave_active_with_master() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SLAVE_ACTIVE_WITH_MASTER) != 0
}
/// monster.conf `retaliate_to_master: yes`, `mob_changetarget_byskill: no`.
pub fn retaliate_to_master() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::RETALIATE_TO_MASTER) != 0
}
pub fn mob_changetarget_byskill() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MOB_CHANGETARGET_BYSKILL) != 0
}
/// monster.conf `monster_class_change_full_recover: yes` (battle_config.monster_class_change_recover).
pub fn monster_class_change_recover() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_CLASS_CHANGE_FULL_RECOVER) != 0
}
/// monster.conf `override_mob_names: 0`.
pub fn override_mob_names() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::OVERRIDE_MOB_NAMES)
}
/// monster.conf `zeny_from_mobs: no`, `mobs_level_up: no`, `mobs_level_up_exp_rate: 1`.
pub fn zeny_from_mobs() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ZENY_FROM_MOBS) != 0
}
pub fn mobs_level_up() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MOBS_LEVEL_UP) != 0
}
pub fn mobs_level_up_exp_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOBS_LEVEL_UP_EXP_RATE)
}
/// monster.conf `mob_remove_damaged: yes`, `mob_remove_delay: 300000`.
pub fn mob_remove_damaged() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MOB_REMOVE_DAMAGED) != 0
}
pub fn mob_remove_delay() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_REMOVE_DELAY) as i64
}
/// monster.conf `mob_npc_event_type: 1`.
pub fn mob_npc_event_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_NPC_EVENT_TYPE)
}
/// monster.conf `ksprotection: 0`.
pub fn ksprotection() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::KSPROTECTION)
}
/// monster.conf `mvp_tomb_enabled: yes`, `mvp_tomb_delay: 9000`.
pub fn mvp_tomb_enabled() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MVP_TOMB_ENABLED) != 0
}
pub fn mvp_tomb_delay() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MVP_TOMB_DELAY) as i64
}
/// monster.conf `mob_size_influence: no`.
pub fn mob_size_influence() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MOB_SIZE_INFLUENCE) != 0
}
/// monster.conf `mob_icewall_walk_block: 75`, `boss_icewall_walk_block: 0`.
pub fn mob_icewall_walk_block() -> u8 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_ICEWALL_WALK_BLOCK) as u8
}
pub fn boss_icewall_walk_block() -> u8 {
    crate::battle_config::get(crate::battle_config::Bc::BOSS_ICEWALL_WALK_BLOCK) as u8
}
/// monster.conf `monster_stuck_warning: no` (battle_config.mob_stuck_warning).
pub fn mob_stuck_warning() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MONSTER_STUCK_WARNING) != 0
}
/// monster.conf `mob_nopc_idleskill_rate: 100`, `boss_nopc_idleskill_rate: 100`, `mob_nopc_move_rate: 100`,
/// `boss_nopc_move_rate: 100`.
pub fn mob_nopc_idleskill_rate() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_NOPC_IDLESKILL_RATE) as u32
}
pub fn boss_nopc_idleskill_rate() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::BOSS_NOPC_IDLESKILL_RATE) as u32
}
pub fn mob_nopc_move_rate() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_NOPC_MOVE_RATE) as u32
}
pub fn boss_nopc_move_rate() -> u32 {
    crate::battle_config::get(crate::battle_config::Bc::BOSS_NOPC_MOVE_RATE) as u32
}
/// monster.conf `achievement_mob_share: no`.
pub fn achievement_mob_share() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ACHIEVEMENT_MOB_SHARE) != 0
}
/// monster.conf `mob_unlock_time: 2000`.
pub fn mob_unlock_time() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_UNLOCK_TIME) as i64
}
/// battle.conf `mob_critical_rate: 100`.
pub fn mob_critical_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MOB_CRITICAL_RATE)
}
/// battle.conf `delay_battle_damage: yes`.
pub fn delay_battle_damage() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DELAY_BATTLE_DAMAGE) != 0
}
/// skill.conf `clear_skills_on_warp: 15` (battle_config.clear_unit_onwarp, BL_PC|BL_MOB|BL_PET|BL_HOM).
pub fn clear_unit_onwarp() -> u16 {
    crate::battle_config::get(crate::battle_config::Bc::CLEAR_SKILLS_ON_WARP) as u16
}
/// item.conf `dead_branch_active: yes`; monster.conf `random_monster_checklv: no`.
pub fn dead_branch_active() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DEAD_BRANCH_ACTIVE) != 0
}
pub fn random_monster_checklv() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::RANDOM_MONSTER_CHECKLV) != 0
}

// drops.conf
/// drops.conf `item_auto_get: no`, `first_attack_loot_bonus: 30`, `mvp_to_loot_priority: no`,
/// `item_drop_mvp_mode: 0`, `drop_rate0item: no`, `drops_by_luk: 0`, `drops_by_luk2: 0`,
/// `alchemist_summon_reward: 1`, `rare_drop_announce: 0`, `autoloot_adjust: 0`, `mercenary_autoloot: no`,
/// `mer_idle_no_share: no`, `drop_rate_cap: 9000`, `drop_rate_cap_vip: 9000`, `enable_bonus_map_drops: yes`.
pub fn item_auto_get() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ITEM_AUTO_GET) != 0
}
pub fn first_attack_loot_bonus() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::FIRST_ATTACK_LOOT_BONUS) as i64
}
pub fn mvp_to_loot_priority() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MVP_TO_LOOT_PRIORITY) != 0
}
pub fn item_drop_mvp_mode() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::ITEM_DROP_MVP_MODE)
}
pub fn drop_rate0item() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DROP_RATE0ITEM) != 0
}
pub fn drops_by_luk() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DROPS_BY_LUK)
}
pub fn drops_by_luk2() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DROPS_BY_LUK2)
}
pub fn alchemist_summon_reward() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::ALCHEMIST_SUMMON_REWARD)
}
pub fn rare_drop_announce() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::RARE_DROP_ANNOUNCE)
}
pub fn autoloot_adjust() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::AUTOLOOT_ADJUST) != 0
}
pub fn mercenary_autoloot() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::MERCENARY_AUTOLOOT) != 0
}
pub const MER_IDLE_NO_SHARE: i32 = 0;
pub fn drop_rate_cap() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DROP_RATE_CAP)
}
pub fn drop_rate_cap_vip() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::DROP_RATE_CAP_VIP)
}
pub fn enable_bonus_map_drops() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ENABLE_BONUS_MAP_DROPS) != 0
}

// exp.conf
/// exp.conf `exp_bonus_attacker: 25`, `exp_bonus_max_attacker: 12`.
pub fn exp_bonus_attacker() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::EXP_BONUS_ATTACKER)
}
pub fn exp_bonus_max_attacker() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::EXP_BONUS_MAX_ATTACKER)
}

// homunc.conf / pet.conf / player.conf
/// homunc.conf `homunculus_exp_gain: 10`, `homunculus_autoloot: no`, `hom_idle_no_share: no`.
pub fn homunculus_exp_gain() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_EXP_GAIN)
}
pub fn homunculus_autoloot() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::HOMUNCULUS_AUTOLOOT) != 0
}
pub const HOM_IDLE_NO_SHARE: i32 = 0;
/// pet.conf `pet_attack_exp_rate: 100`, `pet_attack_exp_to_master: no`.
pub fn pet_attack_exp_rate() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::PET_ATTACK_EXP_RATE)
}
pub fn pet_attack_exp_to_master() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::PET_ATTACK_EXP_TO_MASTER) != 0
}
/// player.conf `idle_no_autoloot: 0`.
pub fn idle_no_autoloot() -> i64 {
    crate::battle_config::get(crate::battle_config::Bc::IDLE_NO_AUTOLOOT) as i64
}
/// misc.conf `taekwon_mission_mobname: 0`, `fame_taekwon_mission: 1`; skill.conf `allow_skill_without_day: no`.
pub fn taekwon_mission_mobname() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::TAEKWON_MISSION_MOBNAME)
}
pub fn fame_taekwon_mission() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FAME_TAEKWON_MISSION)
}
pub fn allow_skill_without_day() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ALLOW_SKILL_WITHOUT_DAY) != 0
}
/// homunc.conf `hom_setting: 0x3D` (homunculus.hpp enum homun_setting: HOMSET_FIRST_TARGET is 0x04, which 0x3D has:
/// "Mobs will always go after them instead of players until attacked"; homunculus.rs keeps the other bits).
pub fn hom_setting() -> i32 {
    crate::homunculus::hom_setting()
}
pub const HOMSET_FIRST_TARGET: i32 = crate::homunculus::data::HOMSET_FIRST_TARGET;

#[cfg(test)]
mod tests {
    use super::*;

    fn flag(name: &str) -> i64 {
        crate::script::battle_flags::BATTLE_FLAGS.iter().find(|(n, _)| *n == name).map(|(_, v)| *v as i64).unwrap_or_else(|| panic!("no battle flag {name}"))
    }

    /// The values the monster skills read equal what the reference map-server runs with (script/battle_flags.rs).
    #[test]
    fn mob_skill_values_match_the_reference_server() {
        let checks: &[(&str, i64)] = &[
            ("monster_ai", mob_ai() as i64),
            ("mob_skill_rate", mob_skill_rate() as i64),
            ("mob_skill_delay", mob_skill_delay() as i64),
            ("mob_changetarget_byskill", mob_changetarget_byskill() as i64),
            ("mob_nopc_idleskill_rate", mob_nopc_idleskill_rate() as i64),
            ("boss_nopc_idleskill_rate", boss_nopc_idleskill_rate() as i64),
        ];
        for (name, v) in checks {
            assert_eq!(flag(name), *v, "battle_config {name}");
        }
    }
}
