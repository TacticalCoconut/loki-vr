//! skills/gunslinger/spiralshooting.cpp: class SkillSpiralShooting : public SkillImpl (NW_SPIRAL_SHOOTING), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::{W_GRENADE, W_RIFLE};
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSpiralShooting;

impl SkillImpl for SkillSpiralShooting {
    fn skill_id(&self) -> u16 {
        NW_SPIRAL_SHOOTING
    }

    /// SkillSpiralShooting::modifyDamageData: one more hit with a grenade launcher.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if super::weapontype1(c, src) == Some(W_GRENADE) {
            dmg.div_ += 1;
        }
    }

    /// SkillSpiralShooting::castendDamageId: the splash hit (flag & 1); the cast: the animation, castendDamageId
    /// (flag | 1) on the enemies around the target (the splash 2 larger with a grenade launcher), then the aim count
    /// ends.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        } else {
            let mut splash = skill_get_splash(id, skill_lv) as i16;
            if super::weapontype1(c, src) == Some(W_GRENADE) {
                splash += 2;
            }
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, splash, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
            super::end_intensive_aim_count(c, &s);
        }
    }

    /// SkillSpiralShooting::calculateSkillRatio: 1200 + 1700% per level + 5 x CON, + 150% x level per aim count, + 200
    /// + 1100% per level with a rifle, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1200 + 1700 * lv;
        *skillratio += 5 * sstatus.con as i32;
        if let Some(n) = super::intensive_aim_count(c, src) {
            *skillratio += n * 150 * lv;
        }
        if super::weapontype1(c, src) == Some(W_RIFLE) {
            *skillratio += 200 + 1100 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
