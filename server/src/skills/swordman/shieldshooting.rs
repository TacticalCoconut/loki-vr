//! skills/swordman/shieldshooting.cpp: class SkillShieldShooting : public SkillImplRecursiveDamageSplash
//! (IG_SHIELD_SHOOTING).

use crate::battle::calc::Damage;
use crate::db::items::IT_ARMOR;
use crate::db::skills::*;
use crate::inventory::EQI_HAND_L;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillShieldShooting;

impl SkillImpl for SkillShieldShooting {
    fn skill_id(&self) -> u16 {
        IG_SHIELD_SHOOTING
    }

    crate::splash_skill_defaults!();

    /// SkillShieldShooting::calculateSkillRatio (the shield's (weight * 7 / 6) / 10 is uint32 arithmetic).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1000 + 3500 * lv;
        *skillratio += 10 * sstatus.pow as i32;
        *skillratio += lv * 150 * pc_checkskill(c, src, IG_SHIELD_MASTERY);
        if let Some(sd) = crate::battle::bl::sd(c, src) {
            // Damage affected by the shield's weight and refine. Need official formula. [Rytech]
            if let Some((row, data)) = crate::battle::bl::equip(c, &sd, EQI_HAND_L) {
                if data.r#type == IT_ARMOR {
                    *skillratio = (*skillratio as u32).wrapping_add(data.weight.wrapping_mul(7) / 6 / 10) as i32;
                    *skillratio += row.item.refine as i32 * 100;
                }
            }
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillShieldShooting {
    /// SkillShieldShooting::splashSearch: the animation and the caster's status, then the splash.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or(s);
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
