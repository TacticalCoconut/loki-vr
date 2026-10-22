//! skills/acolyte/thirdpunish.cpp: class SkillThirdPunish : public SkillImplRecursiveDamageSplash (IQ_THIRD_PUNISH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state::pc_addspiritball;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_RAISINGDRAGON, SC_SECOND_BRAND};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillThirdPunish;

impl SkillImpl for SkillThirdPunish {
    fn skill_id(&self) -> u16 {
        IQ_THIRD_PUNISH
    }

    crate::splash_skill_defaults!();

    /// SkillThirdPunish::calculateSkillRatio: 450% + 1800% per level + 10 x POW (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 450 + 1800 * skill_lv as i32;
        *skillratio += 10 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillThirdPunish::applyAdditionalEffects: the target's SC_SECOND_BRAND ends.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::status_change_end(c, target.id, SC_SECOND_BRAND, 0);
    }
}

impl RecursiveDamageSplash for SkillThirdPunish {
    /// SkillThirdPunish::splashSearch: the animation on the target; a player fills its spheres up to 5 (+ Raising
    /// Dragon's level); then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        if src.kind == BL_PC {
            // (uint8 limit)
            let mut limit: u8 = 5;
            if let Some(e) = sce(c, src, SC_RAISINGDRAGON) {
                limit = limit.wrapping_add(e.val1 as u8);
            }
            for _ in 0..limit {
                pc_addspiritball(c, src.id, skill_get_time(id, skill_lv), limit as i32);
            }
        }
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
