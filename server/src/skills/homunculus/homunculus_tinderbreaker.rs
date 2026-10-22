//! skills/homunculus/homunculus_tinderbreaker.cpp: class SkillTinderBreaker : public SkillImpl (MH_TINDER_BREAKER).
//! Its damage is battle_calc_skill_base_damage's ATK_ADD (battle/calc.rs).

use super::{fresh, yommy_duration};
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_nodamage, clif_skill_poseffect};
use crate::skills::SkillImpl;
use crate::status::consts::SC_TINDER_BREAKER2;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTinderBreaker;

impl SkillImpl for SkillTinderBreaker {
    fn skill_id(&self) -> u16 {
        MH_TINDER_BREAKER
    }

    /// SkillTinderBreaker::castendDamageId: the homunculus jumps onto the target (unit_movepos easy 1 with path
    /// check; the slide and the ground effect when it moved), SC_TINDER_BREAKER2 on the target for the Yommy
    /// formula's duration (the skill packet with its result), then the hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let duration = yommy_duration(c, src, target, skill_lv); // Yommy formula
        if crate::unit::movepos(c, src.id, target.x, target.y, 1, true) {
            let s = fresh(c, src);
            crate::battle::clif::clif_blown(c, &s);
            clif_skill_poseffect(c, &s, id, skill_lv, target.x, target.y, tick);
        }
        let s = fresh(c, src);
        let ok = sc::sc_start4(c, Some(&s), target, SC_TINDER_BREAKER2, 100, skill_lv as i32, s.id as i32, 0, 0, duration as i64);
        let t = fresh(c, target);
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, ok);
        let (s, t) = (fresh(c, src), fresh(c, target));
        crate::skill::attack::skill_attack(c, skill_get_type(id), &s, &s, &t, id, skill_lv, tick, *flag);
    }
}
