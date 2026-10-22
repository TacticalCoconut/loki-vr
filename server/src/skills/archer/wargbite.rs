//! skills/archer/wargbite.cpp: class SkillWargBite : public WeaponSkillImpl (RA_WUGBITE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::CellChk;
use crate::skill::clif::skill_fail;
use crate::skills::novice::pc_checkskill;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_BITE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWargBite;

impl SkillImpl for SkillWargBite {
    fn skill_id(&self) -> u16 {
        RA_WUGBITE
    }

    /// SkillWargBite::calculateSkillRatio: `base_skillratio += 300 + 200 * skill_lv`, + 100 at level 5.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 300 + 200 * skill_lv as i32;
        if skill_lv == 5 {
            *base_skillratio += 100;
        }
    }

    /// SkillWargBite::castendDamageId: the weapon hit when a walk path reaches the target (path_search, easy,
    /// CELL_CHKNOREACH), else a player's failure packet.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if crate::path::path_search(src.map, src.x, src.y, target.x, target.y, 1, CellChk::ChkNoReach).is_some() {
            weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
        } else if src.kind == crate::map::BL_PC {
            skill_fail(c, src, self.skill_id());
        }
    }

    /// SkillWargBite::applyAdditionalEffects: SC_BITE with (50 + 10 * lv) + 2 * (Tooth of Warg * 2) - AGI / 4, at
    /// least 50 (a non-player counts skill_get_max(RA_TOOTHOFWUG) instead of its double level), for skill_get_time +
    /// 500 ms per Tooth of Warg level (+ skill_get_max for a non-player).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let is_pc = src.kind == crate::map::BL_PC;
        let tooth = pc_checkskill(c, src, RA_TOOTHOFWUG);
        let agi = crate::status::status_get_status_data(c, target).agi as i32;
        let mut wug_rate = (50 + 10 * skill_lv as i32) + 2 * (if is_pc { tooth * 2 } else { skill_get_max(RA_TOOTHOFWUG) }) - (agi / 4);
        if wug_rate < 50 {
            wug_rate = 50;
        }
        let duration = skill_get_time(self.skill_id(), skill_lv) + if is_pc { tooth * 500 } else { skill_get_max(RA_TOOTHOFWUG) };
        sc::sc_start(c, Some(src), target, SC_BITE, wug_rate, skill_lv as i32, duration as i64);
    }
}
