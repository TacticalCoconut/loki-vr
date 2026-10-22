//! skills/gunslinger/midnightfallen.cpp: class SkillMidnightFallen : public WeaponSkillImpl (NW_MIDNIGHT_FALLEN),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::{W_GATLING, W_GRENADE, W_SHOTGUN};
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_HIDDEN_CARD;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMidnightFallen;

impl SkillImpl for SkillMidnightFallen {
    fn skill_id(&self) -> u16 {
        NW_MIDNIGHT_FALLEN
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillMidnightFallen::castendPos2: castendDamageId (flag | 1) on the enemies of the square around (x, y)
    /// (map_foreachinallarea); the splash is 1 larger with a gatling gun, 2 with a grenade launcher (sd->status.weapon).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut splash = skill_get_splash(id, skill_lv) as i16;
        match super::status_weapon(c, src) {
            Some(W_GATLING) => splash += 1,
            Some(W_GRENADE) => splash += 2,
            _ => {}
        }
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - splash, y - splash, x + splash, y + splash, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
    }

    /// SkillMidnightFallen::calculateSkillRatio: 2500 + 850% per level, under Hidden Card + 200 / 340 / 400% per level
    /// with a gatling gun / grenade launcher / shotgun, + 5 x CON, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 2500 + 850 * lv;
        if let Some(w) = super::weapontype1(c, src) {
            if sce(c, src, SC_HIDDEN_CARD).is_some() {
                if w == W_GATLING {
                    *skillratio += 200 * lv;
                } else if w == W_GRENADE {
                    *skillratio += 340 * lv;
                } else if w == W_SHOTGUN {
                    *skillratio += 400 * lv;
                }
            }
        }
        *skillratio += 5 * sstatus.con as i32; // !TODO: check con ratio (rAthena)
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
