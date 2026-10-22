//! skills/mage/fireball.cpp: class SkillFireBall : public SkillImplRecursiveDamageSplash (MG_FIREBALL).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::area;
use crate::skills::novice::splash_damage_default;
use crate::skills::{splash_castend_damage_id, splash_castend_pos2, RecursiveDamageSplash, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFireBall;

impl SkillImpl for SkillFireBall {
    fn skill_id(&self) -> u16 {
        MG_FIREBALL
    }

    /// SkillFireBall::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        // RENEWAL (pre-renewal's `+= -30 + 10 * skill_lv` dropped)
        *base_skillratio += 40 + 20 * skill_lv as i32;
        if wd.miscflag == 2 {
            //Enemies at the edge of the area will take 75% of the damage
            *base_skillratio = *base_skillratio * 3 / 4;
        }
    }

    /// SkillFireBall::castendDamageId: SkillImplRecursiveDamageSplash::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (inherited).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }
}

impl RecursiveDamageSplash for SkillFireBall {
    /// SkillFireBall::splashDamage.
    fn splash_damage(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
        let mut flag = flag;
        // For players, the distance between original target and splash target determines the damage
        if src.kind == BL_PC {
            // map_id2bl(skill_area_temp[1])
            if let Some(orig_bl) = unit::get(c, area::temp(1) as u32) {
                flag |= crate::mob::unit::distance_bl(&orig_bl, target);
            }
        }
        // Call default implementation
        splash_damage_default(c, self.skill_id(), src, target, skill_lv, tick, flag)
    }
}
