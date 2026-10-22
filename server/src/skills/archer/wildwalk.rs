//! skills/archer/wildwalk.cpp: class SkillWildWalk : public WeaponSkillImpl (WH_WILD_WALK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWildWalk;

impl SkillImpl for SkillWildWalk {
    fn skill_id(&self) -> u16 {
        WH_WILD_WALK
    }

    /// SkillWildWalk::calculateSkillRatio: -100 + 1800 + 2800 * lv + 5 * CON, + 10% per Nature Friendly level, + 10%
    /// per Steel Crow level (!TODO: unknown con and WH_NATUREFRIENDLY/HT_STEELCROW skills ratio), RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1800 + 2800 * skill_lv as i32;
        *skillratio += 5 * sstatus.con as i32;
        *skillratio += *skillratio * pc_checkskill(c, src, WH_NATUREFRIENDLY) / 10;
        *skillratio += *skillratio * pc_checkskill(c, src, HT_STEELCROW) / 10;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillWildWalk::castendDamageId: the no-damage packet, the weapon hit, then the skill's status on the caster.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        weapon_castend_damage_id(c, id, src, &super::fresh(c, target), skill_lv, tick, *flag);
        let s = super::fresh(c, src);
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
