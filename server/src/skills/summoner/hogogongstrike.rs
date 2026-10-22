//! skills/summoner/hogogongstrike.cpp: class SkillHogogongStrike : public SkillImpl (SH_HOGOGONG_STRIKE).

use super::communes;
use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_HOGOGONG;
use crate::status::hp::status_heal;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHogogongStrike;

impl SkillImpl for SkillHogogongStrike {
    fn skill_id(&self) -> u16 {
        SH_HOGOGONG_STRIKE
    }

    /// SkillHogogongStrike::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 180 + 200 * lv;
        *skillratio += 10 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        *skillratio += 5 * sstatus.pow as i32;
        if communes(c, src, SH_COMMUNE_WITH_CHUL_HO) {
            *skillratio += 70 + 150 * lv;
            *skillratio += 10 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHogogongStrike::castendDamageId: only the splash calls hit, and only targets under SC_HOGOGONG.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 && sce(c, target, SC_HOGOGONG).is_some() {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
    }

    /// SkillHogogongStrike::castendNoDamageId: 1 AP with the Chul Ho communion, the packet, then the splash
    /// (skill_castend_damage_id on every character around the target).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if communes(c, src, SH_COMMUNE_WITH_CHUL_HO) {
            status_heal(c, src.id, 0, 0, 1, 0);
        }
        area::set_temp(0, 0);
        area::set_temp(1, target.id as i32);
        area::set_temp(2, 0);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_range(c, &t, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| {
            area::skill_area_sub(c, bl, &s, id, skill_lv, tick, f, skill_castend_damage_id)
        });
    }
}
