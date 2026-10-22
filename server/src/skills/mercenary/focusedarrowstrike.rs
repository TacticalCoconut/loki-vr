//! skills/mercenary/mercenary_focusedarrowstrike.cpp: class SkillMercenaryFocusedArrowStrike : public SkillImpl
//! (MA_SHARPSHOOTING).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::skill::area::{self, map_foreachindir, map_foreachinpath, skill_attack_area};
use crate::skill::attack::skill_attack;
use crate::skill::conf;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryFocusedArrowStrike;

impl SkillImpl for SkillMercenaryFocusedArrowStrike {
    fn skill_id(&self) -> u16 {
        MA_SHARPSHOOTING
    }

    /// SkillMercenaryFocusedArrowStrike::calculateSkillRatio (RENEWAL).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 300 + 300 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillMercenaryFocusedArrowStrike::castendDamageId: skill_attack_area on the path from the caster towards the
    /// target (skill_eightpath_algorithm: the eight-direction path, and the target itself when the path hits
    /// nothing; else the line to the target).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let f = *flag;
        area::set_temp(1, target.id as i32);
        let atk_type = skill_get_type(id);
        let splash = skill_get_splash(id, skill_lv) as i16;
        let maxcount = skill_get_maxcount(id, skill_lv);
        if conf::skill_eightpath_algorithm() {
            // Use official AoE algorithm
            let n = map_foreachindir(c, src.map, src.x, src.y, target.x, target.y, splash, maxcount, 0, area::splash_target(src), |c, bl| {
                skill_attack_area(c, bl, atk_type, src, src, id, skill_lv, tick, f, BCT_ENEMY)
            });
            if n == 0 {
                // These skills hit at least the target if the AoE doesn't hit
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                skill_attack(c, atk_type, &s, &s, &t, id, skill_lv, tick, f);
            }
        } else {
            map_foreachinpath(c, src.map, src.x, src.y, target.x, target.y, splash, maxcount, area::splash_target(src), |c, bl| {
                skill_attack_area(c, bl, atk_type, src, src, id, skill_lv, tick, f, BCT_ENEMY)
            });
        }
    }
}
