//! skills/thief/darkillusion.cpp: class SkillDarkIllusion : public WeaponSkillImpl (GC_DARKILLUSION).

use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDarkIllusion;

impl SkillImpl for SkillDarkIllusion {
    fn skill_id(&self) -> u16 {
        GC_DARKILLUSION
    }

    /// SkillDarkIllusion::castendDamageId: the caster moves two cells past the target (seen from the caster) and
    /// hits; with (4 x level)% a Cross Impact follows.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let dir = crate::map::calc_dir(src, target.x, target.y) as i16;
        let x: i16 = if dir > 0 && dir < 4 {
            2
        } else if dir > 4 {
            -2
        } else {
            0
        };
        let y: i16 = if dir > 2 && dir < 6 {
            2
        } else if dir == 7 || dir < 2 {
            -2
        } else {
            0
        };
        if unit::movepos(c, src.id, target.x + x, target.y + y, 1, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            weapon_castend_damage_id(c, id, &s, &t, skill_lv, tick, *flag);
            if ((c.rnd() % 100) as i32) < 4 * skill_lv as i32 {
                let s = unit::get(c, src.id).unwrap_or(s);
                let Some(t) = unit::get_or_freed(c, target.id) else { return };
                skill_castend_damage_id(c, &s, &t, GC_CROSSIMPACT, skill_lv, tick, *flag);
            }
        }
    }
}
