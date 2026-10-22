//! skills/acolyte/adoramus.cpp: class SkillAdoramus : public SkillImplRecursiveDamageSplash (AB_ADORAMUS).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::{re_lvl_dmod, sce, splash_damage_default};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_ADORAMUS, SC_ANCILLA};
use crate::status::defs::ELE_NEUTRAL;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillAdoramus;

impl SkillImpl for SkillAdoramus {
    fn skill_id(&self) -> u16 {
        AB_ADORAMUS
    }

    crate::splash_skill_defaults!();

    /// SkillAdoramus::applyAdditionalEffects: SC_ADORAMUS, rate 4% per level + job level / 2 (50 / 2 for non-players).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let job_level = if src.kind == BL_PC { crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32).unwrap_or(0) } else { 50 };
        sc::sc_start(c, Some(src), target, SC_ADORAMUS, skill_lv as i32 * 4 + job_level / 2, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillAdoramus::calculateSkillRatio: 300% + 250% per level (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 300 + 250 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillAdoramus::modifyElement: neutral under Ancilla.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if sce(c, src, SC_ANCILLA).is_some() {
            *element = ELE_NEUTRAL;
        }
    }
}

impl RecursiveDamageSplash for SkillAdoramus {
    /// SkillAdoramus::splashDamage: no damage should happen if the target is on Land Protector.
    fn splash_damage(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
        if crate::skill::iface::map_getcell_landprotector(c, target.map, target.x, target.y) {
            return 0; // No damage should happen if the target is on Land Protector
        }
        splash_damage_default(c, self.skill_id(), src, target, skill_lv, tick, flag)
    }
}
