//! skills/merchant/rushstrike.cpp: class SkillRushStrike : public SkillImplRecursiveDamageSplash (MT_RUSH_STRIKE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::attack::{skill_blown, BLOWN_NONE};
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::merchant::rushquake::direction_opposite;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillRushStrike::SkillRushStrike() : SkillImplRecursiveDamageSplash(MT_RUSH_STRIKE).
pub struct SkillRushStrike;

impl SkillImpl for SkillRushStrike {
    fn skill_id(&self) -> u16 {
        MT_RUSH_STRIKE
    }
    crate::splash_skill_defaults!();

    /// SkillRushStrike::calculateSkillRatio: 650 + 3750 * lv %, + 5 * POW ("!TODO: check POW ratio"),
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 650 + 3750 * skill_lv as i32;
        *base_skillratio += 5 * sstatus.pow as i32; // !TODO: check POW ratio
        re_lvl_dmod(c, src, base_skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillRushStrike {
    /// SkillRushStrike::splashSearch: jump to the target before attacking (then a 1-cell knockback of the caster
    /// towards direction_opposite(...), rushquake.rs), the skill packet (animation), the caster's position
    /// (clif_blown), then the parent's search.
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
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
