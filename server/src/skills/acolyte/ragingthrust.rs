//! skills/acolyte/ragingthrust.cpp: class SkillRagingThrust : public WeaponSkillImpl (MO_COMBOFINISH).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::novice::sce;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_GT_ENERGYGAIN, SC_SPIRIT};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRagingThrust;

impl SkillImpl for SkillRagingThrust {
    fn skill_id(&self) -> u16 {
        MO_COMBOFINISH
    }

    /// SkillRagingThrust::castendDamageId: becomes a splash attack when Soul Linked (Monk spirit); else
    /// WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 == 0 && sce(c, src, SC_SPIRIT).is_some_and(|e| e.val2 == SL_MONK as i32) {
            // Becomes a splash attack when Soul Linked.
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Always, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        } else {
            weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
        }
    }

    /// SkillRagingThrust::calculateSkillRatio (RENEWAL): +450% + 50% per level + STR; +50% under Gentle Touch -
    /// Energy Gain.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += 450 + 50 * skill_lv as i32 + sstatus.str as i32; // !TODO: How does STR play a role?
        if sce(c, src, SC_GT_ENERGYGAIN).is_some() {
            *base_skillratio += *base_skillratio * 50 / 100;
        }
    }
}
