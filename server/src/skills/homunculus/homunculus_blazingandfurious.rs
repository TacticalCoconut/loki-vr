//! skills/homunculus/homunculus_blazingandfurious.cpp: class SkillBlazingAndFurious : public
//! SkillImplRecursiveDamageSplash (MH_BLAZING_AND_FURIOUS).

use super::hd_of;
use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_WEAPON};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBlazingAndFurious;

impl SkillImpl for SkillBlazingAndFurious {
    fn skill_id(&self) -> u16 {
        MH_BLAZING_AND_FURIOUS
    }

    crate::splash_skill_defaults!();

    /// SkillBlazingAndFurious::modifyDamageData: one hit per spirit sphere of the homunculus (skill_isNotOk_hom keeps
    /// them for this skill; skill_attack removes them all afterwards).
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if let Some(hd) = hd_of(c, src) {
            dmg.div_ = hd.homunculus.spiritball as i16;
        }
    }

    /// SkillBlazingAndFurious::castendNoDamageId: a dash onto an enemy (unit_movepos easy 2 with path check) and the
    /// weapon hit; a player caster gets USESKILL_FAIL on a non-enemy (so it can not be used to move).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // Check if the target is an enemy; if not, skill should fail so the character doesn't unit_movepos (exploitable)
        if crate::battle::target::battle_check_target(c, src, target, BCT_ENEMY) > 0 {
            if crate::unit::movepos(c, src.id, target.x, target.y, 2, true) {
                let s = super::fresh(c, src);
                crate::skill::attack::skill_attack(c, BF_WEAPON, &s, &s, target, id, skill_lv, tick, *flag);
                let s = super::fresh(c, src);
                crate::battle::clif::clif_blown(c, &s);
            }
        } else if src.kind == BL_PC {
            clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
        }
    }

    /// SkillBlazingAndFurious::calculateSkillRatio: -100 + 80 * lv * base level / 100 + STR.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 80 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.str as i32;
    }
}

impl RecursiveDamageSplash for SkillBlazingAndFurious {}
