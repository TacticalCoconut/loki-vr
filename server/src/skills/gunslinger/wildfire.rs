//! skills/gunslinger/wildfire.cpp: class SkillWildFire : public WeaponSkillImpl (NW_WILD_FIRE), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::{W_GRENADE, W_SHOTGUN};
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWildFire;

impl SkillImpl for SkillWildFire {
    fn skill_id(&self) -> u16 {
        NW_WILD_FIRE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillWildFire::castendPos2: castendDamageId (flag | 1) on the enemies of the square around (x, y)
    /// (map_foreachinallarea; 2 larger with a grenade launcher, sd->status.weapon), then the aim count ends.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut i = skill_get_splash(id, skill_lv) as i16;
        if super::status_weapon(c, src) == Some(W_GRENADE) {
            i += 2;
        }
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        super::end_intensive_aim_count(c, &s);
    }

    /// SkillWildFire::calculateSkillRatio: 1500 + 3450% per level + 5 x CON, + 500% x level per aim count, + 100% per
    /// level with a shotgun, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1500 + 3450 * lv;
        *skillratio += 5 * sstatus.con as i32;
        if let Some(n) = super::intensive_aim_count(c, src) {
            *skillratio += n * 500 * lv;
        }
        if super::weapontype1(c, src) == Some(W_SHOTGUN) {
            *skillratio += 100 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
