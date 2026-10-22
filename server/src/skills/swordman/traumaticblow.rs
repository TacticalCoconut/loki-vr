//! skills/swordman/traumaticblow.cpp: class SkillTraumaticBlow : public WeaponSkillImpl (LK_HEADCRUSH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::skill_fail;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BLEEDING;
use crate::status::defs::{CLASS_BOSS, RC_DEMON};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTraumaticBlow;

impl SkillImpl for SkillTraumaticBlow {
    fn skill_id(&self) -> u16 {
        LK_HEADCRUSH
    }

    /// SkillTraumaticBlow::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 40 * skill_lv as i32;
    }

    /// SkillTraumaticBlow::castendDamageId: fails on bosses.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if crate::battle::bl::class_type(c, target) == CLASS_BOSS {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            return;
        }
        weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
    }

    /// SkillTraumaticBlow::applyAdditionalEffects: Headcrush has chance of causing Bleeding status, except on demon
    /// and undead element.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if !(sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) || tstatus.race as i32 == RC_DEMON) {
            sc::sc_start2(c, Some(src), target, SC_BLEEDING, 50, skill_lv as i32, src.id as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
        }
    }
}
