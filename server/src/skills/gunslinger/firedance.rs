//! skills/gunslinger/firedance.cpp: class SkillFireDance : public SkillImplRecursiveDamageSplash (RL_FIREDANCE),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFireDance;

impl SkillImpl for SkillFireDance {
    fn skill_id(&self) -> u16 {
        RL_FIREDANCE
    }

    crate::splash_skill_defaults!();

    /// SkillFireDance::calculateSkillRatio: + 100 + 100% per level, + 20% per Desperado level (players),
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 100 + 100 * skill_lv as i32;
        *skillratio += if src.kind == BL_PC { pc_checkskill(c, src, GS_DESPERADO) * 20 } else { 0 };
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillFireDance::castendNoDamageId: the animation, then skill_castend_damage_id (the splash).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillFireDance {}
