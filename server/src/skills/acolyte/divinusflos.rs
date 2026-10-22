//! skills/acolyte/divinusflos.cpp: class SkillDivinusFlos : public SkillImplRecursiveDamageSplash (CD_DIVINUS_FLOS).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_ANCILLA;
use crate::status::defs::ELE_NEUTRAL;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDivinusFlos;

impl SkillImpl for SkillDivinusFlos {
    fn skill_id(&self) -> u16 {
        CD_DIVINUS_FLOS
    }

    crate::splash_skill_defaults!();

    /// SkillDivinusFlos::calculateSkillRatio: 4000% per level + 70% per Fidus Animus level + 10 x SPL
    /// (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 4000 * skill_lv as i32;
        *skillratio += 70 * pc_checkskill(c, src, CD_FIDUS_ANIMUS);
        *skillratio += 10 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillDivinusFlos::modifyElement: neutral under Ancilla.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if sce(c, src, SC_ANCILLA).is_some() {
            *element = ELE_NEUTRAL;
        }
    }
}

impl RecursiveDamageSplash for SkillDivinusFlos {
    /// SkillDivinusFlos::splashSearch: the animation on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
