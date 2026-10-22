//! skills/ninja/kocrossslash.cpp: class SkillKoCrossSlash : public WeaponSkillImpl (KO_JYUMONJIKIRI), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_JYUMONJIKIRI, SC_KAGEMUSYA};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillKoCrossSlash;

impl SkillImpl for SkillKoCrossSlash {
    fn skill_id(&self) -> u16 {
        KO_JYUMONJIKIRI
    }

    /// SkillKoCrossSlash::modifyDamageData: on a target already marked by the skill the hit count is negated
    /// (`dmg.div_ *= -1`, rAthena's TODO: needs more info).
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, _src: &Unit, target: &Unit, _skill_lv: u16) {
        if sce(c, target, SC_JYUMONJIKIRI).is_some() {
            dmg.div_ = dmg.div_.wrapping_mul(-1);
        }
    }

    /// SkillKoCrossSlash::applyAdditionalEffects: the target is marked (SC_JYUMONJIKIRI).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_JYUMONJIKIRI, 100, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillKoCrossSlash::calculateSkillRatio: 200% per level, RE_LVL_DMOD(120), + level x base level on a marked
    /// target, then Shadow Warrior's bonus.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 200 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 120);
        if sce(c, target, SC_JYUMONJIKIRI).is_some() {
            *skillratio += skill_lv as i32 * crate::status::status_get_lv(c, src);
        }
        if let Some(e) = sce(c, src, SC_KAGEMUSYA) {
            *skillratio += *skillratio * e.val2 / 100;
        }
    }

    /// SkillKoCrossSlash::castendDamageId: the caster moves 2 cells past the target (unit_movepos easy 1, path
    /// checked), and only then strikes (WeaponSkillImpl).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let dir = crate::map::calc_dir(src, target.x, target.y) as i16;
        let x: i16 = if dir > 0 && dir < 4 {
            2
        } else if dir > 4 {
            -2
        } else {
            0
        };
        let y: i16 = if dir > 2 && dir < 6 {
            2
        } else if dir == 7 || dir < 2 {
            -2
        } else {
            0
        };
        if unit::movepos(c, src.id, target.x + x, target.y + y, 1, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
            let t = unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
            weapon_castend_damage_id(c, self.skill_id(), &s, &t, skill_lv, tick, *flag);
        }
    }
}
