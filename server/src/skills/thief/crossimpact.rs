//! skills/thief/crossimpact.cpp: class SkillCrossImpact : public WeaponSkillImpl (GC_CROSSIMPACT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::path::{DIRX, DIRY, DIR_NORTHEAST};
use crate::skill::clif::clif_skill_fail;
use crate::skill::clif::fail::USESKILL_FAIL;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCrossImpact;

impl SkillImpl for SkillCrossImpact {
    fn skill_id(&self) -> u16 {
        GC_CROSSIMPACT
    }

    /// SkillCrossImpact::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 1400 + 150 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillCrossImpact::castendDamageId: the caster jumps next to the target (on its side towards the caster) and
    /// hits; a failed move is USESKILL_FAIL.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut dir = DIR_NORTHEAST;
        if target.x != src.x || target.y != src.y {
            dir = crate::map::calc_dir(target, src.x, src.y); // dir based on target as we move player based on target location
        }
        if skill_check_unit_movepos(c, 0, src, target.x + DIRX[dir as usize], target.y + DIRY[dir as usize], 1, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            weapon_castend_damage_id(c, id, &s, &t, skill_lv, tick, *flag);
        } else if src.kind == BL_PC {
            clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
        }
    }
}
