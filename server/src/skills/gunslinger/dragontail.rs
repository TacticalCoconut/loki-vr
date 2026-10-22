//! skills/gunslinger/dragontail.cpp: class SkillDragonTail : public SkillImplRecursiveDamageSplash (RL_D_TAIL),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SKILL_ALTDMG_FLAG;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_C_MARKER;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDragonTail;

impl SkillImpl for SkillDragonTail {
    fn skill_id(&self) -> u16 {
        RL_D_TAIL
    }

    crate::splash_skill_defaults!();

    /// SkillDragonTail::calculateSkillRatio: 500 + 200% per level, doubled on a target the caster marked
    /// (SKILL_ALTDMG_FLAG), RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 500 + 200 * skill_lv as i32;
        if wd.miscflag & SKILL_ALTDMG_FLAG != 0 {
            *skillratio *= 2;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillDragonTail {
    /// SkillDragonTail::getSplashTarget: characters only.
    fn get_splash_target(&self, _c: &Ctx, _src: &Unit) -> u16 {
        BL_CHAR
    }

    /// SkillDragonTail::splashSearch: a target marked by this caster adds SKILL_ALTDMG_FLAG to skill_area_temp[0]
    /// (TODO in rAthena: do we need to check the src id?); the skill animation is disabled (skill_area_temp[1] = 0);
    /// the animation on the target, then the parent's.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        if let Some(e) = sce(c, target, SC_C_MARKER) {
            if e.val2 == src.id as i32 {
                area::set_temp(0, area::temp(0) | SKILL_ALTDMG_FLAG);
            }
        }
        // Disable skill animation
        area::set_temp(1, 0);
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
