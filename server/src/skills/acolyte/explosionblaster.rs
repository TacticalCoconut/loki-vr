//! skills/acolyte/explosionblaster.cpp: class SkillExplosionBlaster : public SkillImplRecursiveDamageSplash
//! (IQ_EXPOSION_BLASTER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_HOLY_OIL;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillExplosionBlaster;

impl SkillImpl for SkillExplosionBlaster {
    fn skill_id(&self) -> u16 {
        IQ_EXPOSION_BLASTER
    }

    crate::splash_skill_defaults!();

    /// SkillExplosionBlaster::castendNoDamageId: the animation, then the splash through skill_castend_damage_id.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_castend_damage_id(c, src, &t, id, skill_lv, tick, *flag);
    }

    /// SkillExplosionBlaster::calculateSkillRatio: 450% + 2600% per level + 10 x POW; +950% per level on a target
    /// under Holy Oil (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 450 + 2600 * skill_lv as i32;
        *skillratio += 10 * sstatus.pow as i32;
        if sce(c, target, SC_HOLY_OIL).is_some() {
            *skillratio += 950 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillExplosionBlaster {}
