//! skills/ninja/finalstrike.cpp: class SkillFinalStrike : public WeaponSkillImpl (NJ_ISSEN), rAthena e985006.

use crate::battle::BF_MISC;
use crate::clif::Target;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_HIDING, SC_NEN};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFinalStrike;

impl SkillImpl for SkillFinalStrike {
    fn skill_id(&self) -> u16 {
        NJ_ISSEN
    }

    /// SkillFinalStrike::castendDamageId (RENEWAL): the caster slides 2 cells past the target (not in GvG:
    /// skill_check_unit_movepos flag 5), the misc hit, then HP drops to 1% of the maximum (at least 1), Ninja Aura and
    /// Hiding end.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let dir = crate::map::calc_dir(src, target.x, target.y) as i16;
        let i: i16 = 2; // Move 2 cells (From target)
        let x = if dir > 0 && dir < 4 {
            -i
        } else if dir > 4 {
            i
        } else {
            0
        };
        let y = if dir > 2 && dir < 6 {
            -i
        } else if dir == 7 || dir < 2 {
            i
        } else {
            0
        };
        // Doesn't have slide effect in GVG
        if skill_check_unit_movepos(c, 5, src, target.x + x, target.y + y, 1, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
            crate::skill::clif::clif_spiritball(c, &s, None, Target::Area);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
        skill_attack(c, BF_MISC, &s, &s, &t, self.skill_id(), skill_lv, tick, *flag);
        let s = unit::get(c, src.id).unwrap_or(s);
        let max_hp = crate::status::status_get_status_data(c, &s).max_hp;
        crate::status::hp::status_set_hp(c, src.id, (max_hp / 100).max(1), 0);
        sc::status_change_end(c, src.id, SC_NEN, 0);
        sc::status_change_end(c, src.id, SC_HIDING, 0);
    }
}
