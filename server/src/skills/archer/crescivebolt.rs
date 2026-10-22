//! skills/archer/crescivebolt.cpp: class SkillCresciveBolt : public WeaponSkillImpl (WH_CRESCIVE_BOLT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_CALAMITYGALE, SC_CRESCIVEBOLT};
use crate::status::defs::{RC_BRUTE, RC_FISH};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCresciveBolt;

impl SkillImpl for SkillCresciveBolt {
    fn skill_id(&self) -> u16 {
        WH_CRESCIVE_BOLT
    }

    /// SkillCresciveBolt::calculateSkillRatio: -100 + 500 + 1300 * lv + 5 * CON, RE_LVL_DMOD(100), then + 20% per
    /// Crescive Bolt stack and + 20% under Calamity Gale (+ 50% more against brutes and fish).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        *skillratio += -100 + 500 + 1300 * skill_lv as i32;
        *skillratio += 5 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        // if (sc) { … } (sce answers None for a unit without a status_change)
        if let Some(e) = sce(c, src, SC_CRESCIVEBOLT) {
            *skillratio += *skillratio * (20 * e.val1) / 100;
        }
        if sce(c, src, SC_CALAMITYGALE).is_some() {
            *skillratio += *skillratio * 20 / 100;
            if tstatus.race as i32 == RC_BRUTE || tstatus.race as i32 == RC_FISH {
                *skillratio += *skillratio * 50 / 100;
            }
        }
    }

    /// SkillCresciveBolt::castendDamageId: the no-damage packet, the weapon hit, then the caster's Crescive Bolt
    /// stack grows (at most 3).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        weapon_castend_damage_id(c, id, src, &super::fresh(c, target), skill_lv, tick, *flag);
        let s = super::fresh(c, src);
        let time = skill_get_time(id, skill_lv) as i64;
        if let Some(e) = sce(c, &s, SC_CRESCIVEBOLT) {
            sc::sc_start(c, Some(&s), &s, SC_CRESCIVEBOLT, 100, 3.min(1 + e.val1), time);
        } else {
            sc::sc_start(c, Some(&s), &s, SC_CRESCIVEBOLT, 100, 1, time);
        }
    }
}
