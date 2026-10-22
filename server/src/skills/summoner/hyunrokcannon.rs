//! skills/summoner/hyunrokcannon.cpp: class SkillHyunrokCannon : public SkillImpl (SH_HYUN_ROK_CANNON).

use super::{colors_of_hyun_rok_element, communes};
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHyunrokCannon;

impl SkillImpl for SkillHyunrokCannon {
    fn skill_id(&self) -> u16 {
        SH_HYUN_ROK_CANNON
    }

    /// SkillHyunrokCannon::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1450 + 2250 * lv;
        *skillratio += 50 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        *skillratio += 5 * sstatus.spl as i32;
        if communes(c, src, SH_COMMUNE_WITH_HYUN_ROK) {
            *skillratio += 450 * lv;
            *skillratio += 25 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHyunrokCannon::castendDamageId: the packet, then the attack of the skill's type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillHyunrokCannon::modifyElement.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        colors_of_hyun_rok_element(c, src, element);
    }
}
