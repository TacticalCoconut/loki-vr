//! skills/ninja/shadowdance.cpp: class SkillShadowDance : public SkillImpl (SS_KAGENOMAI), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_WOS;
use crate::db::skills::*;
use crate::skill::SKILL_ALTDMG_FLAG;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillShadowDance;

impl SkillImpl for SkillShadowDance {
    fn skill_id(&self) -> u16 {
        SS_KAGENOMAI
    }

    /// SkillShadowDance::calculateSkillRatio: 750 + 900% per level, + 70% x level per Shadow Hunting level, + 5 x POW,
    /// RE_LVL_DMOD(100); a mirage's hit (SKILL_ALTDMG_FLAG) deals 30%.
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 750 + 900 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, SS_KAGEGARI) * 70 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if wd.miscflag & SKILL_ALTDMG_FLAG != 0 {
            *skillratio = *skillratio * 3 / 10;
        }
    }

    /// SkillShadowDance::castendDamageId: the splash hit (flag & 1).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::splash_hit(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillShadowDance::castendNoDamageId: the mirages cast it too (skill_mirage_cast), then the animation and
    /// castendDamageId (flag | 1) on the enemies around the target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        super::skill_mirage_cast(c, src, None, id, skill_lv, 0, 0, tick, *flag | BCT_WOS);
        super::nodamage_splash(c, id, src, target, skill_lv, tick, *flag);
    }
}
