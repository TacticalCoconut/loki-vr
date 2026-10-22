//! skills/merchant/crazyweed.cpp: class SkillCrazyWeed : public SkillImpl (GN_CRAZYWEED); class SkillCrazyWeedAttack :
//! public SkillImpl (GN_CRAZYWEED_ATK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillCrazyWeed::SkillCrazyWeed() : SkillImpl(GN_CRAZYWEED).
pub struct SkillCrazyWeed;

impl SkillImpl for SkillCrazyWeed {
    fn skill_id(&self) -> u16 {
        GN_CRAZYWEED
    }

    /// SkillCrazyWeed::castendPos2: 3 + level / 2 GN_CRAZYWEED_ATK timerskills, 150 ms apart, each on a random cell
    /// of GN_CRAZYWEED_ATK's splash square around the target cell (x roll, then y roll).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let area = skill_get_splash(GN_CRAZYWEED_ATK, skill_lv);
        for i in 0..(3 + (skill_lv as i32 / 2)) {
            let x1 = x as i32 - area + (c.rnd() % (area * 2 + 1) as u32) as i32;
            let y1 = y as i32 - area + (c.rnd() % (area * 2 + 1) as u32) as i32;
            skill_addtimerskill(c, src, tick + i as Tick * 150, 0, x1 as i16, y1 as i16, GN_CRAZYWEED_ATK, skill_lv, -1, 0);
        }
    }
}

/// SkillCrazyWeedAttack::SkillCrazyWeedAttack() : SkillImpl(GN_CRAZYWEED_ATK).
pub struct SkillCrazyWeedAttack;

impl SkillImpl for SkillCrazyWeedAttack {
    fn skill_id(&self) -> u16 {
        GN_CRAZYWEED_ATK
    }

    /// SkillCrazyWeedAttack::calculateSkillRatio: 700% + 100% per level, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 700 + 100 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
