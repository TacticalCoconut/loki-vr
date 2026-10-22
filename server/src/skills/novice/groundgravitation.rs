//! skills/novice/groundgravitation.cpp: class SkillGroundGravitation : public SkillImpl (HN_GROUND_GRAVITATION).

use super::{pc_checkskill, re_lvl_dmod, repeat_count, sce};
use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL};
use crate::skill::timerskill::skill_addtimerskill;
use crate::skill::{iface, SD_SPLASH, SKILL_ALTDMG_FLAG, SKILL_NOCONSUME_REQ};
use crate::skills::SkillImpl;
use crate::status::consts::SC_RULEBREAK;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillGroundGravitation;

impl SkillImpl for SkillGroundGravitation {
    fn skill_id(&self) -> u16 {
        HN_GROUND_GRAVITATION
    }

    /// SkillGroundGravitation::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, 0, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillGroundGravitation::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        if mflag & SKILL_ALTDMG_FLAG != 0 {
            // Initial damage
            *skillratio += -100 + 3000 + 1500 * lv;
            *skillratio += pc_checkskill(c, src, HN_SELFSTUDY_SOCERY) * 4 * lv;
            *skillratio += 5 * sstatus.spl as i32;
        } else {
            // Gravitational field damage
            *skillratio += -100 + 800 + 700 * lv;
            *skillratio += pc_checkskill(c, src, HN_SELFSTUDY_SOCERY) * 2 * lv;
            *skillratio += 2 * sstatus.spl as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
        // After RE_LVL_DMOD calculation, HN_SELFSTUDY_SOCERY amplifies the skill ratio of HN_GROUND_GRAVITATION
        // (gravity field damage) by (skill level)%
        if mflag & SKILL_ALTDMG_FLAG == 0 {
            *skillratio += *skillratio * pc_checkskill(c, src, HN_SELFSTUDY_SOCERY) / 100;
        }
        // SC_RULEBREAK increases the skill ratio after HN_SELFSTUDY_SOCERY
        if sce(c, src, SC_RULEBREAK).is_some() {
            *skillratio += *skillratio * 50 / 100;
        }
    }

    /// SkillGroundGravitation::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
    }

    /// SkillGroundGravitation::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if iface::map_getcell_landprotector(c, src.map, x, y) {
            if src.kind == BL_PC {
                clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let splash = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | SD_SPLASH | SKILL_ALTDMG_FLAG | 1;
        foreach_in_area(c, src.map, x - splash, y - splash, x + splash, y + splash, BL_CHAR, Wall::Config, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        iface::skill_unitsetting(c, &s, id, skill_lv, x, y, *flag);
        let interval = skill_get_unit_interval(id);
        for i in 1..=repeat_count(skill_get_time(id, skill_lv), interval) {
            let s = unit::get(c, src.id).unwrap_or_else(|| s.clone());
            skill_addtimerskill(c, &s, tick + i as i64 * interval as i64, 0, x, y, id, skill_lv, 0, *flag);
        }
    }

    /// SkillGroundGravitation::modifyDamageData.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        if dmg.miscflag & SKILL_ALTDMG_FLAG != 0 {
            // Initial damage
            dmg.div_ = -2;
        }
    }
}
