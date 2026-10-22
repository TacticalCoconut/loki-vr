//! skills/mage/sightrasher.cpp: class SkillSightRasher : public SkillImpl (WZ_SIGHTRASHER).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_MAGIC};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_ANIMATION;
use crate::skills::SkillImpl;
use crate::status::consts::SC_SIGHT;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSightRasher;

impl SkillImpl for SkillSightRasher {
    fn skill_id(&self) -> u16 {
        WZ_SIGHTRASHER
    }

    /// SkillSightRasher::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        //Passive side of the attack.
        sc::status_change_end(c, src.id, SC_SIGHT, 0);
        let src = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let target = if target.id == src.id { src.clone() } else { unit::get(c, target.id).unwrap_or_else(|| target.clone()) };
        clif_skill_nodamage(c, Some(&src), &target, id, skill_lv as i32, true);
        // map_foreachinshootrange(skill_area_sub, src, skill_get_splash(id, lv), BL_CHAR|BL_SKILL, src, id, lv, tick,
        // flag|BCT_ENEMY|SD_ANIMATION|1, skill_castend_damage_id)
        let f = *flag | BCT_ENEMY | SD_ANIMATION | 1;
        foreach_in_range(c, &src, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Always, |c, bl| {
            area::skill_area_sub(c, bl, &src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
    }

    /// SkillSightRasher::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillSightRasher::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 20 * skill_lv as i32;
    }
}
