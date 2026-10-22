//! skills/summoner/howlingofchulho.cpp: class SkillHowlingofChulho : public SkillImpl (SH_HOWLING_OF_CHUL_HO).

use super::communes;
use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHowlingofChulho;

impl SkillImpl for SkillHowlingofChulho {
    fn skill_id(&self) -> u16 {
        SH_HOWLING_OF_CHUL_HO
    }

    /// SkillHowlingofChulho::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }

    /// SkillHowlingofChulho::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 600 + 1050 * lv;
        *skillratio += 50 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        *skillratio += 5 * sstatus.pow as i32;
        if communes(c, src, SH_COMMUNE_WITH_CHUL_HO) {
            *skillratio += 100 + 100 * lv;
            *skillratio += 50 * pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY);
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHowlingofChulho::castendDamageId: only the splash calls hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
    }

    /// SkillHowlingofChulho::castendNoDamageId: the splash (1 cell wider with the Chul Ho communion) calls
    /// skill_castend_damage_id on every character around the target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut range = skill_get_splash(id, skill_lv);
        if communes(c, src, SH_COMMUNE_WITH_CHUL_HO) {
            range += 1;
        }
        area::set_temp(0, 0);
        area::set_temp(1, target.id as i32);
        area::set_temp(2, 0);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_range(c, target, range as i16, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
    }
}
