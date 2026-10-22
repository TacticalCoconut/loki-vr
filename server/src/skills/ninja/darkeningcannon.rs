//! skills/ninja/darkeningcannon.cpp: class SkillDarkeningCannon : public SkillImpl (SS_ANTENPOU), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_WOS;
use crate::db::skills::*;
use crate::skill::SKILL_ALTDMG_FLAG;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDarkeningCannon;

impl SkillImpl for SkillDarkeningCannon {
    fn skill_id(&self) -> u16 {
        SS_ANTENPOU
    }

    /// SkillDarkeningCannon::applyAdditionalEffects: the skill's status (skill_get_time2).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillDarkeningCannon::calculateSkillRatio: 450 + 950% per level + 5 x SPL, RE_LVL_DMOD(100); a mirage's hit
    /// (SKILL_ALTDMG_FLAG) deals 30%.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 450 + 950 * skill_lv as i32;
        *skillratio += 5 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if mflag & SKILL_ALTDMG_FLAG != 0 {
            *skillratio = *skillratio * 3 / 10;
        }
    }

    /// SkillDarkeningCannon::castendDamageId: the splash hit (flag & 1).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::splash_hit(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillDarkeningCannon::castendNoDamageId: the mirages cast it too (skill_mirage_cast), then the animation and
    /// castendDamageId (flag | 1) on the enemies around the target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        super::skill_mirage_cast(c, src, None, id, skill_lv, 0, 0, tick, *flag | BCT_WOS);
        super::nodamage_splash(c, id, src, target, skill_lv, tick, *flag);
    }
}
