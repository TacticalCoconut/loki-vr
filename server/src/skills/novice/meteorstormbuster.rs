//! skills/novice/meteorstormbuster.cpp: class SkillMeteorStormBuster : public SkillImpl (HN_METEOR_STORM_BUSTER).
//! The meteors that follow the first one are skill_timerskill's HN_METEOR_STORM_BUSTER case (skill/timerskill.rs).

use super::{pc_checkskill, re_lvl_dmod, repeat_count, sce};
use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_MAGIC};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL};
use crate::skill::timerskill::skill_addtimerskill;
use crate::skill::{iface, SD_SPLASH, SKILL_ALTDMG_FLAG, SKILL_NOCONSUME_REQ};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_RULEBREAK, SC_STUN};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMeteorStormBuster;

impl SkillImpl for SkillMeteorStormBuster {
    fn skill_id(&self) -> u16 {
        HN_METEOR_STORM_BUSTER
    }

    /// SkillMeteorStormBuster::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 3 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillMeteorStormBuster::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        if mflag & SKILL_ALTDMG_FLAG != 0 {
            // Fall damage
            *skillratio += -100 + 300 + 320 * lv;
        } else {
            // Explosion damage
            *skillratio += -100 + 450 + 160 * lv;
        }
        *skillratio += pc_checkskill(c, src, HN_SELFSTUDY_SOCERY) * 5 * lv;
        *skillratio += 3 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        // After RE_LVL_DMOD calculation, HN_SELFSTUDY_SOCERY amplifies the skill ratio of HN_METEOR_STORM_BUSTER (fall
        // damage) by (skill level)%
        if mflag & SKILL_ALTDMG_FLAG != 0 {
            *skillratio += *skillratio * pc_checkskill(c, src, HN_SELFSTUDY_SOCERY) / 100;
        }
        // SC_RULEBREAK increases the skill ratio after HN_SELFSTUDY_SOCERY
        if sce(c, src, SC_RULEBREAK).is_some() {
            *skillratio += *skillratio * 50 / 100;
        }
    }

    /// SkillMeteorStormBuster::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillMeteorStormBuster::castendPos2.
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
        iface::skill_unitsetting(c, &s, id, skill_lv, x, y, skill_get_unit_interval(id));
        let time2 = skill_get_time2(id, skill_lv);
        for i in 1..=repeat_count(skill_get_time(id, skill_lv), time2) {
            let s = unit::get(c, src.id).unwrap_or_else(|| s.clone());
            skill_addtimerskill(c, &s, tick + i as i64 * time2 as i64, 0, x, y, id, skill_lv, 0, *flag);
        }
    }

    /// SkillMeteorStormBuster::modifyDamageData.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        if dmg.miscflag & SKILL_ALTDMG_FLAG != 0 {
            // Fall damage
            dmg.div_ = -3;
        }
    }
}
