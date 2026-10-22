//! skills/gunslinger/missionbombard.cpp: class SkillMissionBombard : public WeaponSkillImpl (NW_MISSION_BOMBARD),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::iface::skill_unitsetting;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skill::SKILL_ALTDMG_FLAG;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, repeat_count};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMissionBombard;

impl SkillImpl for SkillMissionBombard {
    fn skill_id(&self) -> u16 {
        NW_MISSION_BOMBARD
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMissionBombard::castendPos2: the first blast (SKILL_ALTDMG_FLAG, map_foreachinarea over characters and
    /// skill units), the unit, then a timerskill every unit interval (i from 1 to skill_get_time / interval).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | SKILL_ALTDMG_FLAG | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_unitsetting(c, &s, id, skill_lv, x, y, *flag);
        let interval = skill_get_unit_interval(id);
        for i in 1..=repeat_count(skill_get_time(id, skill_lv), interval) {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| s.clone());
            skill_addtimerskill(c, &s, tick + i as i64 * interval as i64, 0, x, y, id, skill_lv, 0, *flag);
        }
    }

    /// SkillMissionBombard::calculateSkillRatio: the first blast (SKILL_ALTDMG_FLAG) 5000 + 1800% per level + 100% per
    /// Grenade Mastery level, the later ones 800 + 200% per level + 30% per Grenade Mastery level; + 5 x CON,
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        if wd.miscflag & SKILL_ALTDMG_FLAG != 0 {
            *skillratio += -100 + 5000 + 1800 * lv;
            *skillratio += pc_checkskill(c, src, NW_GRENADE_MASTERY) * 100;
        } else {
            *skillratio += -100 + 800 + 200 * lv;
            *skillratio += pc_checkskill(c, src, NW_GRENADE_MASTERY) * 30;
        }
        *skillratio += 5 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillMissionBombard::modifyElement: Night Watch Grenade Fragment elementals.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        super::grenade_fragment_element(c, src, element);
    }
}
