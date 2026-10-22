//! skills/acolyte/rideinlightening.cpp: class SkillRideInLightening : public SkillImplRecursiveDamageSplash
//! (SR_RIDEINLIGHTNING).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_castend_damage_id, RecursiveDamageSplash, SkillImpl};
use crate::status::defs::W_KNUCKLE;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRideInLightening;

impl SkillImpl for SkillRideInLightening {
    fn skill_id(&self) -> u16 {
        SR_RIDEINLIGHTNING
    }

    /// SkillImplRecursiveDamageSplash::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillRideInLightening::modifyDamageData: one hit per level for players, 1 otherwise.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, skill_lv: u16) {
        dmg.div_ = if src.kind == BL_PC { (skill_lv as i16).max(1) } else { 1 };
    }

    /// SkillRideInLightening::calculateSkillRatio: 40% per level, +50% per level with a knuckle (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 40 * skill_lv as i32;
        // sd && sd->status.weapon == W_KNUCKLE
        if src.kind == BL_PC && crate::pc::character_of(c, src.id).is_some_and(|ch| ch.weapon as i32 == W_KNUCKLE) {
            *skillratio += 50 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillRideInLightening::castendPos2: every enemy in the splash area (map_foreachinallarea) through
    /// skill_castend_damage_id.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
    }
}

impl RecursiveDamageSplash for SkillRideInLightening {}
