//! skills/thief/counterslash.cpp: class SkillCounterSlash : public SkillImplRecursiveDamageSplash (GC_COUNTERSLASH).

use crate::battle::calc::Damage;
use crate::db::jobs::JOBL_FOURTH;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCounterSlash;

impl SkillImpl for SkillCounterSlash {
    fn skill_id(&self) -> u16 {
        GC_COUNTERSLASH
    }

    crate::splash_skill_defaults!();

    /// SkillCounterSlash::calculateSkillRatio:
    /// ATK [{(Skill Level x 150) + 300} x Caster's Base Level / 120]% + ATK [(AGI x 2) + (Caster's Job Level x 4)]%
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 300 + 150 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 120);
        *skillratio += sstatus.agi as i32 * 2;
        // If 4th job, job level of your 3rd job counts
        let job = if src.kind == BL_PC {
            match crate::pc::character_of(c, src.id) {
                Some(ch) => {
                    if crate::inventory::class_mapid(&ch) & JOBL_FOURTH != 0 {
                        crate::skilltree::Skills::load(c, src.id).map(|s| s.meta.change_level_4th as i32).unwrap_or(0)
                    } else {
                        ch.job_level as i32
                    }
                }
                None => 0,
            }
        } else {
            0
        };
        *skillratio += job * 4;
    }

    /// SkillCounterSlash::castendNoDamageId: the animation, then the damage part.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_castend_damage_id(c, &s, &t, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillCounterSlash {}
