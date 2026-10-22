//! skills/merchant/coldslower.cpp: class SkillColdSlower : public SkillImplRecursiveDamageSplash (NC_COLDSLOWER).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_FREEZE, SC_FREEZING};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillColdSlower::SkillColdSlower() : SkillImplRecursiveDamageSplash(NC_COLDSLOWER).
pub struct SkillColdSlower;

impl SkillImpl for SkillColdSlower {
    fn skill_id(&self) -> u16 {
        NC_COLDSLOWER
    }

    /// SkillImplRecursiveDamageSplash::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillColdSlower::applyAdditionalEffects: "Status chances are applied officially through a check": first
    /// SC_FREEZE (10% per level); when the target is not frozen after it, SC_FREEZING (20% + 10% per level).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, SC_FREEZE, 10 * skill_lv as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        // if (tsc && !tsc->getSCE(SC_FREEZE))
        if let Some(tsc) = crate::battle::bl::sc(c, &t) {
            if !tsc.has(SC_FREEZE) {
                sc::sc_start(c, Some(src), &t, SC_FREEZING, 20 + skill_lv as i32 * 10, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
            }
        }
    }

    /// SkillColdSlower::calculateSkillRatio: +200% + 300% per level, RE_LVL_DMOD(150).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 200 + 300 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 150);
    }

    /// SkillColdSlower::castendPos2: skill_castend_damage_id (flag | BCT_ENEMY | 1) on every character and skill unit
    /// in the splash square (map_foreachinarea), the cast center kept in skill_area_temp[4] / [5].
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // Cast center might be relevant later (e.g. for knockback direction)
        area::set_temp(4, x as i32);
        area::set_temp(5, y as i32);
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
    }
}

impl RecursiveDamageSplash for SkillColdSlower {}
