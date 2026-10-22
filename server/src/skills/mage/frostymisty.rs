//! skills/mage/frostymisty.cpp: class SkillFrostyMisty : public SkillImpl (WL_FROSTMISTY), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{CellChk, BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::SD_ANIMATION;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_FREEZING, SC_MISTY_FROST};
use crate::status::defs::BF_MAGIC;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFrostyMisty;

impl SkillImpl for SkillFrostyMisty {
    fn skill_id(&self) -> u16 {
        WL_FROSTMISTY
    }

    /// SkillFrostyMisty::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 200 + 100 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillFrostyMisty::castendDamageId: Freezing and Misty Frost on every target of the area, damage only where
    /// the caster can shoot.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // Causes Freezing status through walls.
        sc::sc_start(c, Some(src), target, SC_FREEZING, 25 + 5 * skill_lv as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::sc_start(c, Some(src), &t, SC_MISTY_FROST, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        // Doesn't deal damage through non-shootable walls.
        let t = unit::get(c, target.id).unwrap_or(t);
        let wall_check = crate::skill::conf::skill_wall_check();
        if !wall_check || crate::path::path_search(src.map, src.x, src.y, t.x, t.y, 1, CellChk::ChkWall).is_some() {
            skill_attack(c, BF_MAGIC, src, src, &t, id, skill_lv, tick, *flag | SD_ANIMATION);
        }
    }

    /// SkillFrostyMisty::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // Cast center might be relevant later (e.g. for knockback direction)
        area::set_temp(4, x as i32);
        area::set_temp(5, y as i32);
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
    }
}
