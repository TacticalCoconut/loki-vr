//! skills/gunslinger/wildshot.cpp: class SkillWildShot : public SkillImpl (NW_WILD_SHOT), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::{W_REVOLVER, W_RIFLE};
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_HIDDEN_CARD;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWildShot;

impl SkillImpl for SkillWildShot {
    fn skill_id(&self) -> u16 {
        NW_WILD_SHOT
    }

    /// SkillWildShot::castendDamageId: the splash hit (flag & 1); the cast: the animation, then castendDamageId
    /// (flag | 1) on the enemies around the target (the splash 1 larger with a rifle).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        } else {
            let mut splash = skill_get_splash(id, skill_lv) as i16;
            if super::weapontype1(c, src) == Some(W_RIFLE) {
                splash += 1;
            }
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, splash, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        }
    }

    /// SkillWildShot::calculateSkillRatio: 850 + 230% per level, under Hidden Card + 100 / 150% per level with a
    /// revolver / rifle, + 5 x CON, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 850 + 230 * lv;
        if let Some(w) = super::weapontype1(c, src) {
            if sce(c, src, SC_HIDDEN_CARD).is_some() {
                if w == W_REVOLVER {
                    *skillratio += 100 * lv;
                } else if w == W_RIFLE {
                    *skillratio += 150 * lv;
                }
            }
        }
        *skillratio += 5 * sstatus.con as i32; // !TODO: check con ratio (rAthena)
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
