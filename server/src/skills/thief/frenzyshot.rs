//! skills/thief/frenzyshot.cpp: class SkillFrenzyShot : public WeaponSkillImpl (ABC_FRENZY_SHOT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFrenzyShot;

impl SkillImpl for SkillFrenzyShot {
    fn skill_id(&self) -> u16 {
        ABC_FRENZY_SHOT
    }

    /// SkillFrenzyShot::modifyDamageData: three hits at (5 x level)%.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, skill_lv: u16) {
        if c.rnd_chance(5 * skill_lv as i32, 100) {
            dmg.div_ = 3;
        }
    }

    /// SkillFrenzyShot::castendDamageId: the animation, then the hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        weapon_castend_damage_id(c, id, &s, target, skill_lv, tick, *flag);
    }

    /// SkillFrenzyShot::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 350 + 825 * skill_lv as i32;
        *skillratio += 15 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
