//! skills/swordman/overslash.cpp: class SkillOverSlash : public SkillImplRecursiveDamageSplash (IG_OVERSLASH).

use super::pc_checkskill_imperial_guard;
use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillOverSlash;

impl SkillImpl for SkillOverSlash {
    fn skill_id(&self) -> u16 {
        IG_OVERSLASH
    }

    crate::splash_skill_defaults!();

    /// SkillOverSlash::modifyDamageData: 5 hits with 2 targets or more, 7 with 4 or more.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        if dmg.miscflag >= 4 {
            dmg.div_ = 7;
        } else if dmg.miscflag >= 2 {
            dmg.div_ = 5;
        }
    }

    /// SkillOverSlash::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 260 * lv;
        *skillratio += pc_checkskill(c, src, IG_SPEAR_SWORD_M) * 60 * lv;
        *skillratio += 7 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        let i = pc_checkskill_imperial_guard(c, src, 3);
        if i > 0 {
            *skillratio += *skillratio * i / 100;
        }
    }
}

impl RecursiveDamageSplash for SkillOverSlash {
    /// SkillOverSlash::splashSearch: the targets are always counted (skill_area_temp[0]) before the splash.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let n = foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, BCT_ENEMY, area::skill_area_sub_count)
        });
        area::set_temp(0, n);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
