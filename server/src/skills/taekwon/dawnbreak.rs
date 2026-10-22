//! skills/taekwon/dawnbreak.cpp: class SkillDawnBreak : public SkillImplRecursiveDamageSplash (SKE_DAWN_BREAK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_DAWN_MOON, SC_SKY_ENCHANT};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDawnBreak;

impl SkillImpl for SkillDawnBreak {
    fn skill_id(&self) -> u16 {
        SKE_DAWN_BREAK
    }

    crate::splash_skill_defaults!();

    /// SkillDawnBreak::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillDawnBreak::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 750 + 850 * lv;
        *skillratio += pc_checkskill(c, src, SKE_SKY_MASTERY) * 5 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        if sce(c, src, SC_DAWN_MOON).is_some() || sce(c, src, SC_SKY_ENCHANT).is_some() {
            *skillratio += 200 + 200 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillDawnBreak {}
