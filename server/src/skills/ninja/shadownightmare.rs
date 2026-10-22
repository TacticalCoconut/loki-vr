//! skills/ninja/shadownightmare.cpp: class SkillShadowNightmare : public SkillImpl (SS_KAGEAKUMU), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_NIGHTMARE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillShadowNightmare;

impl SkillImpl for SkillShadowNightmare {
    fn skill_id(&self) -> u16 {
        SS_KAGEAKUMU
    }

    /// SkillShadowNightmare::applyAdditionalEffects: the hit ends SC_NIGHTMARE.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::status_change_end(c, target.id, SC_NIGHTMARE, 0);
    }

    /// SkillShadowNightmare::calculateSkillRatio: 22500% + 5 x POW, + half again on a target with SC_NIGHTMARE,
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 22500;
        *skillratio += 5 * sstatus.pow as i32;
        if sce(c, target, SC_NIGHTMARE).is_some() {
            *skillratio += *skillratio / 2;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillShadowNightmare::castendDamageId: the splash hit (flag & 1).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::splash_hit(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillShadowNightmare::castendNoDamageId: the animation, then castendDamageId (flag | 1) on the enemies around
    /// the target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::nodamage_splash(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
