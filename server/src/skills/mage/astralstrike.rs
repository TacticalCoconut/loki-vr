//! skills/mage/astralstrike.cpp: class SkillAstralStrike : public SkillImpl (AG_ASTRAL_STRIKE), class
//! SkillAstralStrikeAttack : public SkillImpl (AG_ASTRAL_STRIKE_ATK), rAthena e985006.
//!
//! AG_ASTRAL_STRIKE_ATK is the damage of the unit group castendPos2 places: skill.cpp skill_unit_onplace_timer's
//! UNT_ASTRAL_STRIKE case calls skill_attack(skill_get_type(AG_ASTRAL_STRIKE_ATK), …) directly
//! (skillunit/ontimer.rs), which asks SkillAstralStrikeAttack for the skill ratio. Its castendDamageId is only
//! reached if the skill itself is cast (by a script or a monster skill).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_MAGIC};
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::iface;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::defs::{RC_DRAGON, RC_UNDEAD};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// AG_ASTRAL_STRIKE
pub struct SkillAstralStrike;

impl SkillImpl for SkillAstralStrike {
    fn skill_id(&self) -> u16 {
        AG_ASTRAL_STRIKE
    }

    /// SkillAstralStrike::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        let lv = skill_lv as i32;

        *skillratio += -100 + 700 + 2600 * lv;
        *skillratio += 10 * sstatus.spl as i32;

        if tstatus.race as i32 == RC_UNDEAD || tstatus.race as i32 == RC_DRAGON {
            *skillratio += 150 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillAstralStrike::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillAstralStrike::castendPos2: the first hit on every enemy of the area, then the unit group.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_get_splash(id, skill_lv);
        let (xi, yi) = (x as i32, y as i32);
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, (xi - i) as i16, (yi - i) as i16, (xi + i) as i16, (yi + i) as i16, BL_CHAR, Wall::None, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
        *flag |= 1;
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        iface::skill_unitsetting(c, &s, id, skill_lv, x, y, 0);
    }
}

/// AG_ASTRAL_STRIKE_ATK
pub struct SkillAstralStrikeAttack;

impl SkillImpl for SkillAstralStrikeAttack {
    fn skill_id(&self) -> u16 {
        AG_ASTRAL_STRIKE_ATK
    }

    /// SkillAstralStrikeAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 650 * skill_lv as i32 + 10 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillAstralStrikeAttack::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}
