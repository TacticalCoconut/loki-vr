//! skills/mage/elementalbuster.cpp: class SkillElementalBuster : public SkillImpl (EM_ELEMENTAL_BUSTER) and the five
//! element classes SkillElementalBusterFire / Ground / Poison / Water / Wind : public SkillImplRecursiveDamageSplash
//! (EM_ELEMENTAL_BUSTER_FIRE / _GROUND / _POISON / _WATER / _WIND), rAthena e985006.
//!
//! EM_ELEMENTAL_BUSTER picks the element class from the player's elemental (`sd->ed`, a Diluvio .. Serpens one: the
//! **mercelem** agent's, elemental.cpp, read through skills/mage/elem_iface.rs, whose stubs answer "no elemental"
//! until then, so the cast ends in clif_skill_fail with SKILL_NOCONSUME_REQ, as rAthena's does for a player without
//! one) and casts it on every enemy around the target (skill_castend_damage_id with flag | SD_LEVEL | SD_SPLASH | 1:
//! the element class's inherited SkillImplRecursiveDamageSplash::castendDamageId, recursive hit).

use super::elem_iface::{ed_class, sd_ed};
use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::misc::elemental::{ELEMENTALID_ARDOR, ELEMENTALID_DILUVIO, ELEMENTALID_PROCELLA, ELEMENTALID_SERPENS, ELEMENTALID_TERREMOTUS};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::{SD_LEVEL, SD_SPLASH, SKILL_NOCONSUME_REQ};
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::defs::{RC_DRAGON, RC_FORMLESS};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// EM_ELEMENTAL_BUSTER
pub struct SkillElementalBuster;

impl SkillImpl for SkillElementalBuster {
    fn skill_id(&self) -> u16 {
        EM_ELEMENTAL_BUSTER
    }

    /// SkillElementalBuster::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src);
        if src.kind != BL_PC {
            return;
        }
        let sd = src;

        // !sd->ed || !(sd->ed->elemental.class_ >= ELEMENTALID_DILUVIO &&
        //     sd->ed->elemental.class_ <= ELEMENTALID_SERPENS)
        // (mercelem agent: elemental.cpp)
        let class_ = sd_ed(c, sd.id).map(|e| ed_class(c, e)).filter(|cl| (ELEMENTALID_DILUVIO..=ELEMENTALID_SERPENS).contains(cl));
        let Some(class_) = class_ else {
            skill_fail(c, sd, id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        };

        let buster_element: u16 = match class_ {
            ELEMENTALID_ARDOR => EM_ELEMENTAL_BUSTER_FIRE,
            ELEMENTALID_DILUVIO => EM_ELEMENTAL_BUSTER_WATER,
            ELEMENTALID_PROCELLA => EM_ELEMENTAL_BUSTER_WIND,
            ELEMENTALID_TERREMOTUS => EM_ELEMENTAL_BUSTER_GROUND,
            ELEMENTALID_SERPENS => EM_ELEMENTAL_BUSTER_POISON,
            // (unreachable: the range check above leaves only these five classes; rAthena's variable would stay
            // uninitialized)
            _ => return,
        };

        area::set_temp(1, 0);
        // Animation for the triggered blaster element.
        clif_skill_nodamage(c, Some(src), target, buster_element, skill_lv as i32, true);
        // Triggered after blaster animation to make correct skill name scream appear.
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let f = *flag | BCT_ENEMY | SD_LEVEL | SD_SPLASH | 1;
        foreach_in_range(c, target, 6, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| {
            area::skill_area_sub(c, bl, src, buster_element, skill_lv, tick, f, skill_castend_damage_id)
        });
    }
}

/// EM_ELEMENTAL_BUSTER_FIRE
pub struct SkillElementalBusterFire;

impl SkillImpl for SkillElementalBusterFire {
    fn skill_id(&self) -> u16 {
        EM_ELEMENTAL_BUSTER_FIRE
    }

    crate::splash_skill_defaults!();

    /// SkillElementalBusterFire::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        buster_skill_ratio(c, src, target, skill_lv, skillratio);
    }
}

impl RecursiveDamageSplash for SkillElementalBusterFire {}

/// EM_ELEMENTAL_BUSTER_GROUND
pub struct SkillElementalBusterGround;

impl SkillImpl for SkillElementalBusterGround {
    fn skill_id(&self) -> u16 {
        EM_ELEMENTAL_BUSTER_GROUND
    }

    crate::splash_skill_defaults!();

    /// SkillElementalBusterGround::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        buster_skill_ratio(c, src, target, skill_lv, skillratio);
    }
}

impl RecursiveDamageSplash for SkillElementalBusterGround {}

/// EM_ELEMENTAL_BUSTER_POISON
pub struct SkillElementalBusterPoison;

impl SkillImpl for SkillElementalBusterPoison {
    fn skill_id(&self) -> u16 {
        EM_ELEMENTAL_BUSTER_POISON
    }

    crate::splash_skill_defaults!();

    /// SkillElementalBusterPoison::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        buster_skill_ratio(c, src, target, skill_lv, skillratio);
    }
}

impl RecursiveDamageSplash for SkillElementalBusterPoison {}

/// EM_ELEMENTAL_BUSTER_WATER
pub struct SkillElementalBusterWater;

impl SkillImpl for SkillElementalBusterWater {
    fn skill_id(&self) -> u16 {
        EM_ELEMENTAL_BUSTER_WATER
    }

    crate::splash_skill_defaults!();

    /// SkillElementalBusterWater::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        buster_skill_ratio(c, src, target, skill_lv, skillratio);
    }
}

impl RecursiveDamageSplash for SkillElementalBusterWater {}

/// EM_ELEMENTAL_BUSTER_WIND
pub struct SkillElementalBusterWind;

impl SkillImpl for SkillElementalBusterWind {
    fn skill_id(&self) -> u16 {
        EM_ELEMENTAL_BUSTER_WIND
    }

    crate::splash_skill_defaults!();

    /// SkillElementalBusterWind::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        buster_skill_ratio(c, src, target, skill_lv, skillratio);
    }
}

impl RecursiveDamageSplash for SkillElementalBusterWind {}

/// The calculateSkillRatio the five element classes share word for word (SkillElementalBusterFire / Ground / Poison /
/// Water / Wind::calculateSkillRatio).
fn buster_skill_ratio(c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32) {
    let sstatus = crate::status::status_get_status_data(c, src);
    let tstatus = crate::status::status_get_status_data(c, target);
    let lv = skill_lv as i32;

    *skillratio += -100 + 550 + 2650 * lv;
    *skillratio += 10 * sstatus.spl as i32;

    if tstatus.race as i32 == RC_FORMLESS || tstatus.race as i32 == RC_DRAGON {
        *skillratio += 150 * lv;
    }
    re_lvl_dmod(c, src, skillratio, 100);
}
