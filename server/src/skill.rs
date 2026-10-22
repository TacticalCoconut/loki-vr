//! map/skill.cpp and the skill parts of pc.cpp / unit.cpp / clif.cpp: learned skills (the character's rows of
//! `char_skill`, rAthena's `skill` table), and the skill-use pipeline in the files below:
//! - `parse.rs`: clif_parse_UseSkillToId / ToPos / ToPosMoreInfo / skill_toid and the other skill requests;
//! - `unit.rs`: unit_skilluse_id(2) / pos(2), unit_skillcastcancel, unit_set_castdelay, unit_cancel_combo;
//! - `castend.rs`: the cast-end timers skill_castend_id / skill_castend_pos, skill_castend_id_check,
//!   skill_castend_damage_id / nodamage_id / pos2 (the shells and their remaining case arms), skill_keep_using;
//! - `attack.rs`: skill_attack, skill_attack_blow, skill_blown, skill_magic_reflect, skill_do_copy;
//! - `effect.rs`: skill_additional_effect, skill_counter_additional_effect, skill_onskillusage, skill_break_equip,
//!   skill_strip_equip;
//! - `condition.rs`: skill_isNotOk, skill_check_condition_castbegin / castend, skill_get_requirement,
//!   skill_consume_requirement;
//! - `castfix.rs`: skill_castfix, skill_castfix_sc, skill_vfcastfix, skill_delayfix, pc_get_skillcooldown;
//! - `cooldown.rs`: skill_blockpc_start / end / clear and the skillcooldown table;
//! - `combo.rs`, `timerskill.rs`, `autospell.rs`, `misc.rs`, `area.rs` (skill_area_sub, skill_area_temp,
//!   map_foreachin*), `clif.rs` (the packets), `state.rs` (map_session_data's skill fields), `conf.rs`.
//! The per-skill classes are in `src/skills/` (the SkillImpl trait and its registry).

pub mod area;
pub mod atcommand;
pub mod attack;
pub mod autospell;
pub mod castend;
pub mod castfix;
pub mod clif;
pub mod combo;
pub mod condition;
pub mod conf;
pub mod cooldown;
pub mod effect;
pub mod homun;
pub mod iface;
pub mod misc;
pub mod parse;
pub mod state;
pub mod timerskill;
pub mod umap;
pub mod unit;

use spacetimedb::table;

use crate::unit::Unit;
use crate::world::Ctx;

/// rAthena `skill` table (sql-files/main.sql): (char_id, id, lv, flag).
#[table(accessor = char_skill, index(accessor = by_char, btree(columns = [char_id])))]
#[derive(Clone, Debug)]
pub struct CharSkill {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub char_id: u32,
    pub skill_id: u16,
    pub lv: u8,
    /// e_skill_flag (SKILL_FLAG_PERMANENT 0, temporary / plagiarised / replaced levels).
    pub flag: u8,
}

// skill.hpp flags passed to skill_attack / skill_area_sub (e_skill_display) and the other flag bits.
/// SD_LEVEL: skill_attack will send -1 instead of skill level (affects display of some skills).
pub const SD_LEVEL: i32 = 0x1000;
/// SD_ANIMATION: skill_attack will use '5' instead of the skill's 'type' (this makes skills show an animation).
/// Also being used in skill_attack for splash skill (NK_SPLASH) to check status_check_skilluse.
pub const SD_ANIMATION: i32 = 0x2000;
/// SD_SPLASH: skill_area_sub will count targets in skill_area_temp[2].
pub const SD_SPLASH: i32 = 0x4000;
/// SD_PREAMBLE: skill_area_sub will transmit a 'magic' damage packet (-30000 dmg) for the first target selected.
pub const SD_PREAMBLE: i32 = 0x8000;
/// skill.hpp NPC_EARTHQUAKE_FLAG: tracks the hitcount of Earthquake for skills that can avoid the first attack.
pub const NPC_EARTHQUAKE_FLAG: i32 = 0x800;
/// skill.hpp SKILL_ALTDMG_FLAG: to control alternative skill scalings.
pub const SKILL_ALTDMG_FLAG: i32 = 0x10;
/// skill.hpp SKILL_NOCONSUME_REQ: make skill ignore requirement consumption [Muh].
pub const SKILL_NOCONSUME_REQ: i32 = 0x20;
/// skill.hpp UNIT_NOCONSUME_AMMO: make skill consume ammo, but not the unit [Muh].
pub const UNIT_NOCONSUME_AMMO: i32 = 0x40;
/// skill.hpp SECURITY_CASTTIME: added to canact delay on castbegin and substracted on castend.
pub const SECURITY_CASTTIME: i64 = 100;
/// skill.hpp MAX_MOBSKILL_LEVEL: max monster skill level (on skill usage).
pub const MAX_MOBSKILL_LEVEL: u16 = 100;

/// pc.cpp pc_checkskill: the level of a skill the character has (0 when not learned, or when the skill is not in the
/// character's current tree: the slot's id is not set — see skilltree.rs).
pub fn checkskill(c: &Ctx, aid: u32, skill_id: u16) -> u8 {
    let Some(p) = crate::pc::get(c, aid) else { return 0 };
    if crate::db::skills::skill_db().get_index(skill_id, true) == 0 {
        log::error!("pc_checkskill: Invalid skill id {skill_id} (char_id={}).", p.char_id);
        return 0;
    }
    if crate::db::skills::skill_chk_guild(skill_id) {
        // guild_checkskill(sd->guild->guild, skill_id) when sd->status.guild_id > 0 && sd->guild
        return crate::guild::pc_checkskill_guild(c, aid, skill_id);
    }
    crate::skilltree::checkskill_rows(c, aid, p.char_id, skill_id)
}

/// clif.hpp useskill_fail_cause USESKILL_FAIL_LEVEL / USESKILL_FAIL_SKILLINTERVAL (kept for the callers of wave C).
pub const USESKILL_FAIL_LEVEL: u8 = clif::fail::USESKILL_FAIL_LEVEL;
pub const USESKILL_FAIL_SKILLINTERVAL: u8 = clif::fail::USESKILL_FAIL_SKILLINTERVAL;

/// conf/battle/skill.conf `display_skill_fail: 2` (skill/conf.rs; atcmd_pc's pc_authok showdelay reads it here).
pub(crate) fn display_skill_fail() -> i32 {
    conf::display_skill_fail()
}

/// clif.cpp clif_skill_fail(sd, skill_id, USESKILL_FAIL_LEVEL, btype).
pub fn skill_fail(c: &Ctx, sd: &Unit, skill_id: u16, btype: i32) {
    clif::clif_skill_fail(c, sd, skill_id, USESKILL_FAIL_LEVEL, btype, 0);
}

/// clif.cpp clif_skill_fail: ZC_ACK_TOUSESKILL (skill/clif.rs).
pub fn skill_fail_ex(c: &Ctx, sd: &Unit, skill_id: u16, cause: u8, btype: i32, item_id: u32) {
    clif::clif_skill_fail(c, sd, skill_id, cause, btype, item_id);
}

/// mmo.hpp e_skill_flag SKILL_FLAG_TEMPORARY (status/defs.rs).
pub use crate::status::defs::SKILL_FLAG_TEMPORARY;

/// A skill level the character holds only for the length of one call (atcommand.cpp MC_CART_MDFY sets
/// sd->status.skill[idx] = { id, lv, SKILL_FLAG_TEMPORARY } around pc_setcart). Returns the row to remove.
pub fn lend_skill(c: &Ctx, aid: u32, skill_id: u16, lv: u8) -> Option<u64> {
    crate::skilltree::lend_skill(c, aid, skill_id, lv)
}

/// Ends a [`lend_skill`] (the slot goes back to { 0, 0, SKILL_FLAG_PERMANENT }).
pub fn unlend_skill(c: &Ctx, row_id: u64) {
    crate::skilltree::unlend_skill(c, row_id);
}
