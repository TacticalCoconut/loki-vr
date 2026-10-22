//! skills/homunculus/homunculus_theonefighterrises.cpp: class SkillTheOneFighterRises : public
//! SkillImplRecursiveDamageSplash (MH_THE_ONE_FIGHTER_RISES).

use super::{fresh, hd_of};
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::homunculus::MAX_SPIRITBALL;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillTheOneFighterRises;

impl SkillImpl for SkillTheOneFighterRises {
    fn skill_id(&self) -> u16 {
        MH_THE_ONE_FIGHTER_RISES
    }

    crate::splash_skill_defaults!();

    /// SkillTheOneFighterRises::castendNoDamageId: a homunculus gains a spirit sphere (hom_addspiritball up to
    /// MAX_SPIRITBALL), then the skill packet and the splash through skill_castend_damage_id.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if hd_of(c, src).is_some() {
            crate::homunculus::hom_addspiritball(c, src.id, MAX_SPIRITBALL);
        }
        let s = fresh(c, src);
        clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, &s, target, id, skill_lv, tick, *flag);
    }

    /// SkillTheOneFighterRises::calculateSkillRatio: -100 + 580 * lv * base level / 100 + STR.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 580 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.str as i32;
    }
}

impl RecursiveDamageSplash for SkillTheOneFighterRises {}
