//! skills/archer/greatecho.cpp: class SkillGreatEcho : public WeaponSkillImpl (WM_GREAT_ECHO).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::skill::area::{foreach_in_area, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGreatEcho;

impl SkillImpl for SkillGreatEcho {
    fn skill_id(&self) -> u16 {
        WM_GREAT_ECHO
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillGreatEcho::calculateSkillRatio: -100 + 250 + 500 * lv; a player adds 50 per Lesson level and doubles it
    /// with a partner around (skill_check_pc_partner, AREA_SIZE, no consumption); RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 250 + 500 * skill_lv as i32;
        if src.kind == crate::map::BL_PC {
            *skillratio += pc_checkskill(c, src, WM_LESSON) * 50; // !TODO: Confirm bonus
            let mut lv = skill_lv;
            if crate::skill::iface::skill_check_pc_partner(c, src, self.skill_id(), &mut lv, crate::conf::area_size() as i32, 0) > 0 {
                *skillratio *= 2;
            }
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillGreatEcho::castendPos2: skill_castend_damage_id on every character in the area
    /// (map_foreachinarea(skill_area_sub, …, flag|BCT_ENEMY|1, skill_castend_damage_id)).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, crate::map::BL_CHAR, Wall::Config, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
    }
}
