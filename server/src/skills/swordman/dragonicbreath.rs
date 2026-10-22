//! skills/swordman/dragonicbreath.cpp: class SkillDragonicBreath : public SkillImplRecursiveDamageSplash
//! (DK_DRAGONIC_BREATH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_DRAGONIC_AURA;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDragonicBreath;

impl SkillImpl for SkillDragonicBreath {
    fn skill_id(&self) -> u16 {
        DK_DRAGONIC_BREATH
    }

    crate::splash_skill_defaults!();

    /// SkillDragonicBreath::calculateSkillRatio. The HP / SP terms are uint32 arithmetic in rAthena
    /// (`skill_lv * (sstatus->max_hp * 25 / 100) * 7) / 100`), added to the int32 ratio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as u32;
        let hp_part = |k: u32| lv.wrapping_mul(sstatus.max_hp.wrapping_mul(25) / 100).wrapping_mul(k) / 100;
        let sp_part = |k: u32| lv.wrapping_mul(sstatus.max_sp).wrapping_mul(k) / 100;
        *skillratio += -100 + 250 + 400 * skill_lv as i32;
        *skillratio += 7 * sstatus.pow as i32;
        if sce(c, src, SC_DRAGONIC_AURA).is_some() {
            *skillratio += 3 * sstatus.pow as i32;
            *skillratio = (*skillratio as u32).wrapping_add(hp_part(7)) as i32;
            *skillratio = (*skillratio as u32).wrapping_add(sp_part(7)) as i32;
        } else {
            *skillratio = (*skillratio as u32).wrapping_add(hp_part(5)) as i32;
            *skillratio = (*skillratio as u32).wrapping_add(sp_part(5)) as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillDragonicBreath {
    /// SkillDragonicBreath::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
