//! skills/ninja/darkdragonnightmare.cpp: class SkillDarkDragonNightmare : public SkillImpl (SS_ANKOKURYUUAKUMU),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::SKILL_ALTDMG_FLAG;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_NIGHTMARE;
use crate::status::defs::ELE_FIRE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDarkDragonNightmare;

impl SkillImpl for SkillDarkDragonNightmare {
    fn skill_id(&self) -> u16 {
        SS_ANKOKURYUUAKUMU
    }

    /// SkillDarkDragonNightmare::applyAdditionalEffects: the hit ends SC_NIGHTMARE.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::status_change_end(c, target.id, SC_NIGHTMARE, 0);
    }

    /// SkillDarkDragonNightmare::calculateSkillRatio: 17500% per level + 5 x SPL, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 17500 * skill_lv as i32;
        *skillratio += 5 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillDarkDragonNightmare::castendDamageId: the splash hit (flag & 1), and a second (fire) hit when the target
    /// still has SC_NIGHTMARE after the first (whose additional effect ends it).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
            let t = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
            if sce(c, &t, SC_NIGHTMARE).is_some() {
                let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
                crate::skill::attack::skill_attack(c, skill_get_type(id), &s, &s, &t, id, skill_lv, tick, *flag | SKILL_ALTDMG_FLAG);
            }
        }
    }

    /// SkillDarkDragonNightmare::castendNoDamageId: the animation, then castendDamageId (flag | 1) on the enemies
    /// around the target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::nodamage_splash(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillDarkDragonNightmare::modifyElement: the second hit (SKILL_ALTDMG_FLAG) is fire.
    fn modify_element(&self, _c: &Ctx, dmg: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if dmg.miscflag & SKILL_ALTDMG_FLAG != 0 {
            *element = ELE_FIRE;
        }
    }
}
