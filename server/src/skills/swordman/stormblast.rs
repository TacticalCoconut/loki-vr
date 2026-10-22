//! skills/swordman/stormblast.cpp: class SkillStormBlast : public SkillImplRecursiveDamageSplash (RK_STORMBLAST).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStormBlast;

impl SkillImpl for SkillStormBlast {
    fn skill_id(&self) -> u16 {
        RK_STORMBLAST
    }

    crate::splash_skill_defaults!();

    /// SkillStormBlast::calculateSkillRatio: ATK = [{Rune Mastery Skill Level + (Caster's STR / 6)} x 100] %.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        // ((sd) ? pc_checkskill(sd,RK_RUNEMASTERY) : 0)
        *skillratio += -100 + (pc_checkskill(c, src, RK_RUNEMASTERY) + sstatus.str as i32 / 6) * 100;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillStormBlast::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillStormBlast {}
