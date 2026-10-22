//! skills/acolyte/knucklearrow.cpp: class SkillKnuckleArrow : public SkillImpl (SR_KNUCKLEARROW).

use crate::battle::calc::Damage;
use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::{dir_ka, set_dir_ka, skill_blown, BLOWN_NONE};
use crate::skill::misc::skill_check_unit_movepos;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skill::SD_LEVEL;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_GT_CHANGE;
use crate::status::defs::CLASS_BOSS;
use crate::status::status_get_lv;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillKnuckleArrow;

impl SkillImpl for SkillKnuckleArrow {
    fn skill_id(&self) -> u16 {
        SR_KNUCKLEARROW
    }

    /// SkillKnuckleArrow::calculateSkillRatio: the knock-back hit (miscflag & 4, from skill_attack's blow into a
    /// wall): ATK [(level x 150) + target's weight in % of its maximum (pc_getpercentweight; rAthena's comment says
    /// 1000 x weight / max) + (target base level x 5) x (caster base level / 150)]%; the hit itself: [(level x 100 + 500) x caster base level / 100]% (+100% per level on
    /// bosses); +30% under Gentle Touch - Change.
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if wd.miscflag & 4 != 0 {
            *skillratio += -100 + 150 * skill_lv as i32 + status_get_lv(c, target) * 5;
            // tsd && tsd->weight: pc_getpercentweight(*tsd)
            if target.kind == BL_PC {
                if let Some(it) = crate::inventory::st(c, target.id) {
                    if it.weight != 0 {
                        *skillratio += crate::status::calc::pc_getpercentweight(&it, it.weight) as i32;
                    }
                }
            }
            re_lvl_dmod(c, src, skillratio, 150);
        } else {
            if crate::battle::bl::class_type(c, target) == CLASS_BOSS {
                *skillratio += 400 + 200 * skill_lv as i32;
            } else {
                // ATK [(Skill Level x 100 + 500) x Caster Base Level / 100] %
                *skillratio += 400 + 100 * skill_lv as i32;
            }
            re_lvl_dmod(c, src, skillratio, 100);
        }
        if sce(c, src, SC_GT_CHANGE).is_some() {
            *skillratio += *skillratio * 30 / 100;
        }
    }

    /// SkillKnuckleArrow::castendDamageId: dir_ka holds the target's direction to the caster before the caster
    /// slides next to the target (skill_check_unit_movepos + a 1-cell blow); the hit is a timerskill 300 ms later
    /// (flag | SD_LEVEL | 2).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        // Holds current direction of bl/target to src/attacker before the src is moved to bl location
        set_dir_ka(crate::map::calc_dir(target, src.x, src.y) as i8);
        // Has slide effect
        if skill_check_unit_movepos(c, 5, src, target.x, target.y, 1, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            // Target position is actually one cell next to the target
            skill_blown(c, &s, &s, 1, (dir_ka() + 4) % 8, BLOWN_NONE);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_addtimerskill(c, &s, tick + 300, target.id, 0, 0, self.skill_id(), skill_lv, BF_WEAPON, *flag | SD_LEVEL | 2);
    }
}
