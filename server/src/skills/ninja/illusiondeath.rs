//! skills/ninja/illusiondeath.cpp: class SkillIllusionDeath : public SkillImpl (KO_JYUSATSU), rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_COMA;
use crate::status::defs::{SCSTART_NOAVOID, SCSTART_NONE, SCSTART_NOTICKDEF};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillIllusionDeath;

impl SkillImpl for SkillIllusionDeath {
    fn skill_id(&self) -> u16 {
        KO_JYUSATSU
    }

    /// SkillIllusionDeath::castendNoDamageId: on a player without the curse, with (45 + 5 x level) + 5 x level - INT / 2
    /// %: the curse, a non-lethal loss of `hp x level x 5` percent (status_percent_damage's int8 rate: rAthena passes
    /// the product truncated to int8), and Coma (10 = 0.1%) when the target's base level is not above the caster's;
    /// else a player's cast fails.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // dstsd && tsc && !tsc->getSCE(type) && rnd()%100 < …
        if target.kind == BL_PC && sce(c, target, type_).is_none() && {
            let int_ = crate::status::status_get_status_data(c, target).int_ as i32;
            // [(Base chance of success) + (Skill Level x 5) - (int32 / 2)]%.
            ((c.rnd() % 100) as i32) < (45 + 5 * skill_lv as i32) + skill_lv as i32 * 5 - int_ / 2
        } {
            let ok = sc::status_change_start(c, Some(src), target, type_, 10000, skill_lv as i32, 0, 0, 0, skill_get_time(id, skill_lv) as i64, SCSTART_NOAVOID | SCSTART_NOTICKDEF, 0);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
            let hp = crate::status::status_get_status_data(c, &t).hp;
            // Does not kill the target. (tstatus->hp * skill_lv * 5: uint32 arithmetic, then the int8 parameter)
            let rate = hp.wrapping_mul(skill_lv as u32).wrapping_mul(5) as i32;
            crate::status::hp::status_percent_damage(c, Some(src), target.id, rate, 0, 0, false);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            if let Some(t) = unit::get(c, target.id) {
                if crate::status::status_get_lv(c, &t) <= crate::status::status_get_lv(c, &s) {
                    sc::status_change_start(c, Some(&s), &t, SC_COMA, 10, skill_lv as i32, 0, src.id as i32, 0, 0, SCSTART_NONE, 0);
                }
            }
        } else if src.kind == BL_PC {
            skill_fail(c, src, id);
        }
    }
}
