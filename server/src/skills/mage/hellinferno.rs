//! skills/mage/hellinferno.cpp: class SkillHellInferno : public SkillImpl (WL_HELLINFERNO), rAthena e985006 (its
//! "TODO: refactor to SkillImplRecursiveDamageSplash" is rAthena's). Every target of the splash takes a fire hit, then
//! 300 ms later a shadow hit: a WL_HELLINFERNO timerskill (skill_timerskill's default case, skill_attack of
//! `skl->type` = BF_MAGIC) whose flag carries 2, which the damage hooks below read as ELE_DARK.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skill::SD_SPLASH;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::defs::{BF_MAGIC, ELE_DARK};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHellInferno;

impl SkillImpl for SkillHellInferno {
    fn skill_id(&self) -> u16 {
        WL_HELLINFERNO
    }

    /// SkillHellInferno::modifyDamageData.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        if dmg.miscflag & 2 != 0 {
            // ELE_DARK
            dmg.div_ = -3;
        }
    }

    /// SkillHellInferno::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, mflag: i32) {
        *skillratio += -100 + 400 * skill_lv as i32;
        if mflag & 2 != 0 {
            // ELE_DARK
            *skillratio += 200 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHellInferno::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, BF_MAGIC, src, src, target, id, skill_lv, tick, *flag);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            skill_addtimerskill(c, &s, tick + 300, target.id, 0, 0, id, skill_lv, BF_MAGIC, *flag | 2);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
            });
        }
    }

    /// SkillHellInferno::modifyElement: the second hit (miscflag & 2) is shadow.
    fn modify_element(&self, _c: &Ctx, dmg: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if dmg.miscflag & 2 != 0 {
            *element = ELE_DARK;
        }
    }
}
