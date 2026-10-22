//! skills/gunslinger/slugshot.cpp: class SkillSlugShot : public WeaponSkillImpl (RL_SLUGSHOT), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSlugShot;

impl SkillImpl for SkillSlugShot {
    fn skill_id(&self) -> u16 {
        RL_SLUGSHOT
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSlugShot::applyAdditionalEffects: a sure stun (skill_get_time2).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillSlugShot::calculateSkillRatio: 1200% per level on a monster, 2000% per level otherwise, times (2 + the
    /// target's size).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, _src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if target.kind == BL_MOB {
            *skillratio += -100 + 1200 * skill_lv as i32;
        } else {
            *skillratio += -100 + 2000 * skill_lv as i32;
        }
        *skillratio *= 2 + tstatus.size as i32;
    }

    /// SkillSlugShot::modifyHitRate: beyond 3 cells, hit falls by (11 - level) per further cell (-10:-9:-8:-7:-6); the
    /// distance is an int8.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, src: &Unit, target: &Unit, skill_lv: u16) {
        let mut dist = crate::mob::unit::distance_bl(src, target) as i8;
        if dist > 3 {
            // Reduce n hitrate for each cell after initial 3 cells. Different each level
            dist -= 3;
            *hit_rate = (*hit_rate as i32 - (11 - skill_lv as i32) * dist as i32) as i16;
        }
    }
}
