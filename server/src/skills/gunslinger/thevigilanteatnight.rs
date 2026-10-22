//! skills/gunslinger/thevigilanteatnight.cpp: class SkillTheVigilanteAtNight : public SkillImpl
//! (NW_THE_VIGILANTE_AT_NIGHT), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::W_GATLING;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTheVigilanteAtNight;

impl SkillImpl for SkillTheVigilanteAtNight {
    fn skill_id(&self) -> u16 {
        NW_THE_VIGILANTE_AT_NIGHT
    }

    /// SkillTheVigilanteAtNight::modifyDamageData: 3 more hits with a gatling gun.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if super::weapontype1(c, src) == Some(W_GATLING) {
            dmg.div_ += 3;
        }
    }

    /// SkillTheVigilanteAtNight::castendNoDamageId: skill_area_temp[0..2] reset (the target in [1]); with a gatling
    /// gun an 11x11 area (i = 5) and the gatling animation, else the skill's splash and the shotgun animation;
    /// castendDamageId (flag | 1) on the enemies around the target, then the aim count ends.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut i = skill_get_splash(id, skill_lv) as i16;
        area::set_temp(0, 0);
        area::set_temp(1, target.id as i32);
        area::set_temp(2, 0);
        if super::weapontype1(c, src) == Some(W_GATLING) {
            i = 5; // 11x11
            clif_skill_nodamage(c, Some(src), target, NW_THE_VIGILANTE_AT_NIGHT_GUN_GATLING, skill_lv as i32, true);
        } else {
            clif_skill_nodamage(c, Some(src), target, NW_THE_VIGILANTE_AT_NIGHT_GUN_SHOTGUN, skill_lv as i32, true);
        }
        let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
        foreach_in_range(c, target, i, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        super::end_intensive_aim_count(c, &s);
    }

    /// SkillTheVigilanteAtNight::castendDamageId: the splash hit (flag & 1).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
    }

    /// SkillTheVigilanteAtNight::calculateSkillRatio: with a gatling gun 350% per level + 100% x level per aim count,
    /// else 850 + 800% per level + 200% x level per aim count; + 5 x CON, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        if super::weapontype1(c, src) == Some(W_GATLING) {
            *skillratio += -100 + 350 * lv;
            if let Some(n) = super::intensive_aim_count(c, src) {
                *skillratio += n * 100 * lv;
            }
        } else {
            *skillratio += -100 + 850 + 800 * lv;
            if let Some(n) = super::intensive_aim_count(c, src) {
                *skillratio += n * 200 * lv;
            }
        }
        *skillratio += 5 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
