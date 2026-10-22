//! skills/novice/shieldchainrush.cpp: class SkillShieldChainRush : public WeaponSkillImpl (HN_SHIELD_CHAIN_RUSH).

use super::{pc_checkskill, re_lvl_dmod};
use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_HNNOWEAPON;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillShieldChainRush;

impl SkillImpl for SkillShieldChainRush {
    fn skill_id(&self) -> u16 {
        HN_SHIELD_CHAIN_RUSH
    }

    /// SkillShieldChainRush::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, 0, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillShieldChainRush::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 600 + 1300 * lv;
        *skillratio += pc_checkskill(c, src, HN_SELFSTUDY_TATICS) * 3 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillShieldChainRush::castendDamageId (rAthena: "TODO : refactor to SkillImplRecursiveDamageSplash").
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
            });
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            sc::sc_start(c, Some(&s), &s, SC_HNNOWEAPON, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        }
    }
}
