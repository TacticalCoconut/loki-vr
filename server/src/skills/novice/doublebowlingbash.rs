//! skills/novice/doublebowlingbash.cpp: class SkillDoubleBowlingBash : public SkillImpl (HN_DOUBLEBOWLINGBASH).

use super::{pc_checkskill, re_lvl_dmod};
use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::SkillImpl;
use crate::status::consts::SC_HNNOWEAPON;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDoubleBowlingBash;

impl SkillImpl for SkillDoubleBowlingBash {
    fn skill_id(&self) -> u16 {
        HN_DOUBLEBOWLINGBASH
    }

    /// SkillDoubleBowlingBash::modifyDamageData.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        if dmg.miscflag > 1 {
            dmg.div_ = (dmg.div_ as i32 + 4.min(dmg.miscflag)) as i16;
        }
    }

    /// SkillDoubleBowlingBash::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 250 + 400 * lv;
        *skillratio += pc_checkskill(c, src, HN_SELFSTUDY_TATICS) * 3 * lv;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillDoubleBowlingBash::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, area::temp(0) & 0xFFF);
        } else {
            let splash = skill_get_splash(id, skill_lv) as i16;
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let n = foreach_in_range(c, target, splash, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, BCT_ENEMY, area::skill_area_sub_count));
            area::set_temp(0, n);
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, splash, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            sc::sc_start(c, Some(&s), &s, SC_HNNOWEAPON, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        }
    }
}
