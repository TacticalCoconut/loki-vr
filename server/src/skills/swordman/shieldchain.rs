//! skills/swordman/shieldchain.cpp: class SkillShieldChain : public WeaponSkillImpl (PA_SHIELDCHAIN).

use crate::battle::calc::Damage;
use crate::db::items::IT_ARMOR;
use crate::db::skills::*;
use crate::inventory::EQI_HAND_L;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_SHIELD_POWER;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillShieldChain;

impl SkillImpl for SkillShieldChain {
    fn skill_id(&self) -> u16 {
        PA_SHIELDCHAIN
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillShieldChain::calculateSkillRatio (RENEWAL: the ratio is set, not added to; the shield's weight / 10 +
    /// 4 * refine is uint32 arithmetic).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let lv = skill_lv as i32;
        let shield_power = sce(c, src, SC_SHIELD_POWER).is_some();
        *skillratio = -100 + 300 + 200 * lv;
        if let Some(sd) = crate::battle::bl::sd(c, src) {
            // Damage affected by the shield's weight and refine.
            if let Some((row, data)) = crate::battle::bl::equip(c, &sd, EQI_HAND_L) {
                if data.r#type == IT_ARMOR {
                    let add = (data.weight / 10).wrapping_add((4 * row.item.refine as i32) as u32);
                    *skillratio = (*skillratio as u32).wrapping_add(add) as i32;
                }
            }
            // Damage affected by shield mastery
            if shield_power {
                *skillratio += lv * 14 * pc_checkskill(c, src, IG_SHIELD_MASTERY);
            }
        }
        re_lvl_dmod(c, src, skillratio, 100);
        if shield_power {
            // Whats the official increase? [Rytech]
            *skillratio += *skillratio * 50 / 100;
        }
    }
}
