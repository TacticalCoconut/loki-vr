//! skills/merchant/rushquake.cpp: class SkillRushQuake : public SkillImplRecursiveDamageSplash (MT_RUSH_QUAKE), and
//! path.cpp direction_opposite (as rAthena declares it, returning a bool), which it and rushstrike.cpp use.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::path::{DIR_CENTER, DIR_MAX};
use crate::skill::attack::{skill_blown, BLOWN_NONE};
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_RUSH_QUAKE1, SC_RUSH_QUAKE2};
use crate::status::defs::{RC_FORMLESS, RC_INSECT};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillRushQuake::SkillRushQuake() : SkillImplRecursiveDamageSplash(MT_RUSH_QUAKE).
pub struct SkillRushQuake;

impl SkillImpl for SkillRushQuake {
    fn skill_id(&self) -> u16 {
        MT_RUSH_QUAKE
    }
    crate::splash_skill_defaults!();

    /// SkillRushQuake::calculateSkillRatio: 3600 * lv % + 10 * POW, + 150 * lv % on formless and insect targets,
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        *base_skillratio += -100 + 3600 * skill_lv as i32 + 10 * sstatus.pow as i32;
        if tstatus.race as i32 == RC_FORMLESS || tstatus.race as i32 == RC_INSECT {
            *base_skillratio += 150 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, base_skillratio, 100);
    }

    /// SkillRushQuake::applyAdditionalEffects: SC_RUSH_QUAKE1 on the target (100%, skill_get_time).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_RUSH_QUAKE1, 100, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }
}

impl RecursiveDamageSplash for SkillRushQuake {
    /// SkillRushQuake::splashSearch: jump to the target before attacking (then a 1-cell knockback of the caster
    /// towards direction_opposite(...), see [`direction_opposite`]), the skill packet (animation), the caster's
    /// position (clif_blown), SC_RUSH_QUAKE2 on the caster (skill_get_time2), then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        // Jump to the target before attacking.
        if skill_check_unit_movepos(c, 5, src, target.x, target.y, 0, true) {
            // src->x / src->y are the new position now
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let dir = direction_opposite(crate::map::calc_dir(target, s.x, s.y) as i8);
            skill_blown(c, &s, &s, 1, dir, BLOWN_NONE);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true); // Trigger animation
        crate::battle::clif::clif_blown(c, &s);
        // TODO: does this buff start before or after dealing damage? [Muh]
        sc::sc_start(c, Some(&s), &s, SC_RUSH_QUAKE2, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or(s);
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}

/// path.cpp direction_opposite. rAthena declares it `bool` (path.hpp), so the opposite direction it computes —
/// `(direction + DIR_MAX / 2) % DIR_MAX`, or DIR_CENTER / DIR_MAX returned as they are — reaches the caller as 0 or 1:
/// DIR_NORTH (0) when the opposite is north (the direction was DIR_SOUTH), DIR_NORTHWEST (1) otherwise.
pub(crate) fn direction_opposite(direction: i8) -> i8 {
    let d = direction as i32;
    let opposite = if direction == DIR_CENTER || d == DIR_MAX as i32 { d } else { (d + DIR_MAX as i32 / 2) % DIR_MAX as i32 };
    (opposite != 0) as i8
}

#[cfg(test)]
mod tests {
    use super::direction_opposite;
    use crate::path::*;

    /// direction_opposite's bool: only DIR_SOUTH (whose opposite is DIR_NORTH) gives 0.
    #[test]
    fn direction_opposite_is_a_bool() {
        assert_eq!(direction_opposite(DIR_SOUTH as i8), 0);
        for d in [DIR_NORTH, DIR_NORTHWEST, DIR_WEST, DIR_SOUTHWEST, DIR_SOUTHEAST, DIR_EAST, DIR_NORTHEAST] {
            assert_eq!(direction_opposite(d as i8), 1, "direction {d}");
        }
        assert_eq!(direction_opposite(DIR_CENTER), 1);
        assert_eq!(direction_opposite(DIR_MAX as i8), 1);
    }
}
