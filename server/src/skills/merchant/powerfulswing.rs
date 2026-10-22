//! skills/merchant/powerfulswing.cpp: class SkillPowerfulSwing : public SkillImplRecursiveDamageSplash
//! (MT_POWERFUL_SWING).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_AXE_STOMP;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillPowerfulSwing::SkillPowerfulSwing() : SkillImplRecursiveDamageSplash(MT_POWERFUL_SWING).
pub struct SkillPowerfulSwing;

impl SkillImpl for SkillPowerfulSwing {
    fn skill_id(&self) -> u16 {
        MT_POWERFUL_SWING
    }
    crate::splash_skill_defaults!();

    /// SkillPowerfulSwing::calculateSkillRatio: 300 + 880 * lv %, + 5 * POW ("!TODO: check POW ratio"),
    /// + 100 + 100 * lv % under the caster's SC_AXE_STOMP, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 300 + 880 * skill_lv as i32;
        *base_skillratio += 5 * sstatus.pow as i32; // !TODO: check POW ratio
        // const status_change* sc = status_get_sc(src); sc && sc->getSCE(SC_AXE_STOMP)
        if sce(c, src, SC_AXE_STOMP).is_some() {
            *base_skillratio += 100 + 100 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, base_skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillPowerfulSwing {
    /// SkillPowerfulSwing::splashSearch: the skill packet on the target first, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
