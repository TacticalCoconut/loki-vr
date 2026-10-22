//! skills/swordman/overbrand.cpp: class SkillOverBrand : public SkillImplRecursiveDamageSplash (LG_OVERBRAND).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_OVERBRANDREADY;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillOverBrand;

impl SkillImpl for SkillOverBrand {
    fn skill_id(&self) -> u16 {
        LG_OVERBRAND
    }

    crate::splash_skill_defaults!();

    /// SkillOverBrand::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillOverBrand::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let lv = skill_lv as i32;
        if sce(c, src, SC_OVERBRANDREADY).is_some() {
            *skillratio += -100 + 500 * lv;
        } else {
            *skillratio += -100 + 350 * lv;
        }
        // ((sd) ? pc_checkskill(sd, CR_SPEARQUICKEN) * 50 : 0)
        *skillratio += pc_checkskill(c, src, CR_SPEARQUICKEN) * 50;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillOverBrand {}
