//! skills/gunslinger/hastyfireinthehole.cpp: class SkillHastyFireInTheHole : public WeaponSkillImpl
//! (NW_HASTY_FIRE_IN_THE_HOLE), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHastyFireInTheHole;

impl SkillImpl for SkillHastyFireInTheHole {
    fn skill_id(&self) -> u16 {
        NW_HASTY_FIRE_IN_THE_HOLE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillHastyFireInTheHole::castendPos2: three blasts: the cast (splash), then 300 ms later (flag 1: splash + 1)
    /// and 600 ms later (flag 3: splash + 2), the later two queued as timerskills without requirements.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut i = skill_get_splash(id, skill_lv) as i16;
        if *flag & 1 != 0 {
            i += 1;
        }
        if *flag & 2 != 0 {
            i += 1;
        }
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        if *flag & 1 == 0 {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            skill_addtimerskill(c, &s, tick + 300, 0, x, y, id, skill_lv, 0, *flag | 1 | SKILL_NOCONSUME_REQ);
            skill_addtimerskill(c, &s, tick + 600, 0, x, y, id, skill_lv, 0, *flag | 3 | SKILL_NOCONSUME_REQ);
        }
    }

    /// SkillHastyFireInTheHole::calculateSkillRatio: 1500 + 1500% per level, + 20% per Grenade Mastery level, + 5 x
    /// CON, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1500 + 1500 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, NW_GRENADE_MASTERY) * 20;
        *skillratio += 5 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHastyFireInTheHole::modifyElement: Night Watch Grenade Fragment elementals.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        super::grenade_fragment_element(c, src, element);
    }
}
