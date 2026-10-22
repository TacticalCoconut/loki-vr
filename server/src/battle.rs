//! map/battle.cpp (rAthena e985006, RENEWAL): who may attack whom (battle_check_target, battle_check_range), the
//! normal-attack damage calculation (battle_calc_weapon_attack and its parts, element / size / race / card fixes,
//! battle_calc_damage, the GvG / battleground / PK rates), the attack itself (battle_weapon_attack) and the delayed
//! damage (battle_delay_damage), plus unit.cpp's attack parts (unit_attack.rs), the player side of dying (pc_dead,
//! respawn) and the PvP / GvG map rules.
//!
//! Submodules: `conf` (the battle_config values battle reads), `target` (battle_check_target / range / master),
//! `unit_attack` (unit_attack, unit_attack_timer, attack / walk delays), `weapon_attack` (battle_weapon_attack),
//! `state` (the `pc_battle` rows: map_session_data fields only battle uses), `mobif` (what the mob agent's system
//! provides), `calc` (battle_calc_weapon_attack and its parts, battle_calc_attack), `magic` (battle_calc_magic_attack),
//! `misc` (battle_calc_misc_attack), `fix` (element / card fixes, battle_calc_damage and the status blocks).
//!
//! Skills: the skill branches of battle.cpp are ported at rAthena's call sites; they call the skill pipeline
//! (`crate::skill::…`: skill_attack, skill_castend_*, skill_additional_effect …) and the skill classes
//! (`crate::skills::skill_impl`: calculateSkillRatio / modifyHitRate / modifyDamageData / modifyElement). What
//! belongs to another system (skill unit groups: skillunit agent; per-SC effects: status2 agent; homunculi,
//! mercenaries, elementals, pets: the next wave) stays a commented stub naming its owner.

pub mod addeff;
pub mod atcommand;
pub mod bl;
pub mod calc;
pub mod clif;
pub mod conf;
pub mod fix;
pub mod knockback;
pub mod magic;
pub mod misc;
pub mod mobif;
pub mod pc_death;
pub mod pvp;
pub mod state;
pub mod target;
pub mod unit_attack;
pub mod weapon_attack;

use crate::world::Ctx;

// ---------------------------------------------------------------------------------------------- battle.hpp enums

/// battle.hpp enum damage_lv.
pub const ATK_NONE: u8 = 0;
pub const ATK_LUCKY: u8 = 1;
pub const ATK_FLEE: u8 = 2;
pub const ATK_MISS: u8 = 3;
pub const ATK_BLOCK: u8 = 4;
pub const ATK_DEF: u8 = 5;

/// battle.hpp enum e_battle_check_target (BCT_SELF, BCT_ENEMY: status/defs.rs; BCT_ALL: db/skills.rs — the same
/// items re-exported, so the glob imports of those modules and this one agree).
pub use crate::db::skills::BCT_ALL;
pub use crate::status::defs::{BCT_ENEMY, BCT_SELF};
pub const BCT_NOONE: i32 = 0x000000;
pub const BCT_PARTY: i32 = 0x040000;
pub const BCT_GUILDALLY: i32 = 0x080000;
pub const BCT_NEUTRAL: i32 = 0x100000;
pub const BCT_SAMEGUILD: i32 = 0x200000;
pub const BCT_WOS: i32 = 0x400000;
pub const BCT_SLAVE: i32 = BCT_SELF | BCT_WOS;
pub const BCT_GUILD: i32 = BCT_SAMEGUILD | BCT_GUILDALLY;
pub const BCT_NOGUILD: i32 = BCT_ALL & !BCT_GUILD;
pub const BCT_NOPARTY: i32 = BCT_ALL & !BCT_PARTY;
pub const BCT_NOENEMY: i32 = BCT_ALL & !BCT_ENEMY;
pub const BCT_ALLY: i32 = BCT_PARTY | BCT_GUILD;
pub const BCT_FRIEND: i32 = BCT_NOENEMY;

/// battle.hpp enum e_battle_flag (status/defs.rs has them: re-exported).
pub use crate::status::defs::{BF_LONG, BF_MAGIC, BF_MISC, BF_NORMAL, BF_RANGEMASK, BF_SHORT, BF_SKILL, BF_SKILLMASK, BF_WEAPON, BF_WEAPONMASK};
pub const BF_NONE: i32 = 0x0000;

/// clif.hpp enum e_damage_type (DMG_NORMAL: db/skills.rs, re-exported).
pub use crate::db::skills::DMG_NORMAL;
pub const DMG_PICKUP_ITEM: u8 = 1;
pub const DMG_SIT_DOWN: u8 = 2;
pub const DMG_STAND_UP: u8 = 3;
pub const DMG_ENDURE: u8 = 4;
pub const DMG_SPLASH: u8 = 5;
pub const DMG_SINGLE: u8 = 6;
pub const DMG_REPEAT: u8 = 7;
pub const DMG_MULTI_HIT: u8 = 8;
pub const DMG_MULTI_HIT_ENDURE: u8 = 9;
pub const DMG_CRITICAL: u8 = 10;
pub const DMG_LUCY_DODGE: u8 = 11;
pub const DMG_TOUCH: u8 = 12;
pub const DMG_MULTI_HIT_CRITICAL: u8 = 13;
pub const DMG_SPLASH_ENDURE: u8 = 14;

/// pc.hpp e_idletime_option IDLE_ATTACK.
pub const IDLE_ATTACK: i32 = 0x0010;

// ---------------------------------------------------------------------------------------------- the client's request

/// clif.cpp clif_parse_ActionRequest_sub, the DMG_NORMAL / DMG_REPEAT branch (parse.rs has already done the dead
/// check, stopped the attack and fixed the disguise id, as the function's head does).
pub fn pc_attack_request(c: &Ctx, aid: u32, target_id: u32, continuous: bool) {
    let Some(sd) = crate::unit::get(c, aid) else { return };
    let Some(p) = crate::pc::get(c, aid) else { return };
    if crate::pc::cant_act(&p, sd.sc.opt1) {
        return;
    }
    if !conf::sdelay_attack_enable() && crate::skill::checkskill(c, aid, crate::db::skills::SA_FREECAST) == 0 && c.tick() - sd.ud.canact_tick < 0 {
        crate::skill::skill_fail_ex(c, &sd, 1, crate::skill::USESKILL_FAIL_SKILLINTERVAL, 0, 0);
        return;
    }
    crate::pc::delinvincibletimer(c, aid);
    if crate::conf::idletime_option() & IDLE_ATTACK != 0 {
        if let Some(mut p) = crate::pc::get(c, aid) {
            p.idletime = crate::pc::last_tick(c);
            crate::pc::put(c, &p);
        }
    }
    // hom_idle_no_share / mer_idle_no_share: both off (homunc.conf / mercenary.conf)
    unit_attack::unit_attack(c, aid, target_id, continuous as i32);
}

/// unit_free for a player: the battle rows go.
pub fn on_unit_free(c: &Ctx, aid: u32) {
    state::free(c, aid);
}
