//! skills/thief/hitandsliding.cpp: class SkillHitAndSliding : public WeaponSkillImpl (ABC_HIT_AND_SLIDING).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::path::{DIRX, DIRY, DIR_NORTHEAST};
use crate::skill::clif::fail::USESKILL_FAIL;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage};
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::defs::{BF_LONG, W_BOW};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHitAndSliding;

impl SkillImpl for SkillHitAndSliding {
    fn skill_id(&self) -> u16 {
        ABC_HIT_AND_SLIDING
    }

    /// SkillHitAndSliding::modifyDamageData: a long-range hit with a bow.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if super::backstab::weapon_of(c, src) == Some(W_BOW) {
            dmg.flag |= BF_LONG;
        }
    }

    /// SkillHitAndSliding::castendDamageId: the caster slides away from the target (total backslide = skill level +
    /// distance between player and target), faces it and hits; the animation in any case.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut dir = DIR_NORTHEAST;
        // Total backslide = skill level + distance between player and target
        let total_backslide = skill_lv as i32 + crate::mob::unit::distance_bl(src, target);
        if target.x != src.x || target.y != src.y {
            dir = crate::map::calc_dir(target, src.x, src.y);
        }
        let dx = target.x as i32 + DIRX[dir as usize] as i32 * total_backslide;
        let dy = target.y as i32 + DIRY[dir as usize] as i32 * total_backslide;
        if skill_check_unit_movepos(c, 0, src, dx as i16, dy as i16, 1, true) {
            let mut s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
            // Set the player's direction to face the target
            let d = crate::map::calc_dir(&s, target.x, target.y);
            unit::setdir(c, &mut s, d, true);
            unit::save(c, &s);
            weapon_castend_damage_id(c, id, &s, target, skill_lv, tick, *flag);
        } else if src.kind == BL_PC {
            //Is this the right behavior? [Haydrich]
            clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
        }
        // Trigger skill animation
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
    }

    /// SkillHitAndSliding::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 3500 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHitAndSliding::applyAdditionalEffects: the skill's status on the caster.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
