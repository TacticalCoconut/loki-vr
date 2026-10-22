//! skills/mage/stormcannon.cpp: class SkillStormCannon : public SkillImpl (AG_STORM_CANNON), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::area;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::npc::acidbreath::breath_castend_damage_id;
use crate::skills::SkillImpl;
use crate::status::consts::SC_CLIMAX;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStormCannon;

impl SkillImpl for SkillStormCannon {
    fn skill_id(&self) -> u16 {
        AG_STORM_CANNON
    }

    /// SkillStormCannon::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;

        *skillratio += -100 + 1550 * lv + 5 * sstatus.spl as i32;

        if sce(c, src, SC_CLIMAX).is_some() {
            *skillratio += 300 * lv;
        }

        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillStormCannon::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        eightpath_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}

/// The castendDamageId body SkillStormCannon and SkillCrimsonArrow share:
/// ```text
/// skill_area_temp[1] = target->id;
/// clif_skill_nodamage(src, *target, getSkillId(), skill_lv);
/// if (battle_config.skill_eightpath_algorithm) { map_foreachindir(skill_attack_area, …) or skill_attack on the
///     target when it hit nothing } else map_foreachinpath(skill_attack_area, …);
/// ```
/// The area part is word for word the breath classes' (skills/npc/acidbreath.rs breath_castend_damage_id, which sets
/// skill_area_temp[1] itself: clif_skill_nodamage does not read it, so the order is the same).
pub(crate) fn eightpath_castend_damage_id(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    area::set_temp(1, target.id as i32);
    clif_skill_nodamage(c, Some(src), target, skill_id, skill_lv as i32, true);
    breath_castend_damage_id(c, skill_id, src, target, skill_lv, tick, flag);
}
