//! skills/homunculus/homunculus_heiligepferd.cpp: class SkillHeiligePferd : public SkillImplRecursiveDamageSplash
//! (MH_HEILIGE_PFERD).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHeiligePferd;

impl SkillImpl for SkillHeiligePferd {
    fn skill_id(&self) -> u16 {
        MH_HEILIGE_PFERD
    }

    crate::splash_skill_defaults!();

    /// SkillHeiligePferd::castendNoDamageId: the skill packet, then the splash through skill_castend_damage_id.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillHeiligePferd::calculateSkillRatio: -100 + 1200 + 350 * lv * base level / 100 + VIT (!TODO upstream:
    /// confirm the VIT bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 1200 + 350 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.vit as i32;
    }
}

impl RecursiveDamageSplash for SkillHeiligePferd {}
