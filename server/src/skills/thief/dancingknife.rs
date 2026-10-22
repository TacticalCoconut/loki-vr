//! skills/thief/dancingknife.cpp: class SkillDancingKnife : public SkillImplRecursiveDamageSplash (SHC_DANCING_KNIFE).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::{SD_LEVEL, SD_SPLASH};
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDancingKnife;

impl SkillImpl for SkillDancingKnife {
    fn skill_id(&self) -> u16 {
        SHC_DANCING_KNIFE
    }

    crate::splash_skill_defaults!();

    /// SkillDancingKnife::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 200 * skill_lv as i32 + 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillDancingKnife::castendNoDamageId: flag&1 (SC_DANCING_KNIFE's periodic hit): the damage part on everything
    /// around the target (forced splash damage); else the status and the animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if *flag & 1 != 0 {
            area::set_temp(1, 0);
            // Note: doesn't force player to stand before attacking
            let f = *flag | BCT_ENEMY | SD_LEVEL | SD_SPLASH;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        } else {
            let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
        }
    }
}

impl RecursiveDamageSplash for SkillDancingKnife {}
