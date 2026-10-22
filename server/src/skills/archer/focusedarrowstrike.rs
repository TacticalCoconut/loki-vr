//! skills/archer/focusedarrowstrike.cpp: class SkillFocusedArrowStrike : public SkillImplRecursiveDamageSplash
//! (SN_SHARPSHOOTING).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_castend_damage_id, splash_castend_pos2, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CAMOUFLAGE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFocusedArrowStrike;

impl SkillImpl for SkillFocusedArrowStrike {
    fn skill_id(&self) -> u16 {
        SN_SHARPSHOOTING
    }

    /// SkillFocusedArrowStrike::calculateSkillRatio: a monster's formula (the splash bonus with wd->miscflag & 2);
    /// Renewal: -100 + 300 + 300 * lv, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if src.kind == crate::map::BL_MOB {
            // TODO: Did these formulas change in the renewal balancing?
            if wd.miscflag & 2 != 0 {
                // Splash damage bonus
                *skillratio += -100 + 140 * skill_lv as i32;
            } else {
                *skillratio += 100 + 50 * skill_lv as i32;
            }
            return;
        }
        *skillratio += -100 + 300 + 300 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillFocusedArrowStrike::castendDamageId (Renewal): SkillImplRecursiveDamageSplash::castendDamageId, and on the
    /// recursive calls the caster's Camouflage ends.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
        if *flag & 1 != 0 {
            sc::status_change_end(c, src.id, SC_CAMOUFLAGE, 0);
        }
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (not overridden).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }
}

impl RecursiveDamageSplash for SkillFocusedArrowStrike {}
