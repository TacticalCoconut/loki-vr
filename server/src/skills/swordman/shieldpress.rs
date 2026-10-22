//! skills/swordman/shieldpress.cpp: class SkillShieldPress : public WeaponSkillImpl (LG_SHIELDPRESS).

use crate::battle::calc::Damage;
use crate::db::items::IT_ARMOR;
use crate::db::skills::*;
use crate::inventory::EQI_HAND_L;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_SHIELD_POWER;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillShieldPress;

impl SkillImpl for SkillShieldPress {
    fn skill_id(&self) -> u16 {
        LG_SHIELDPRESS
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillShieldPress::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let lv = skill_lv as i32;
        *skillratio += -100 + 200 * lv;
        if let Some(sd) = crate::battle::bl::sd(c, src) {
            // Shield Press only considers base STR without job bonus
            *skillratio += sd.ch.str as i32;
            if sce(c, src, SC_SHIELD_POWER).is_some() {
                *skillratio += lv * 15 * pc_checkskill(c, src, IG_SHIELD_MASTERY);
            }
            if let Some((_, data)) = crate::battle::bl::equip(c, &sd, EQI_HAND_L) {
                if data.r#type == IT_ARMOR {
                    *skillratio = (*skillratio as u32).wrapping_add(data.weight / 10) as i32;
                }
            }
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
