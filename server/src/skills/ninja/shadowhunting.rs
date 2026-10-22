//! skills/ninja/shadowhunting.cpp: class SkillShadowHunting : public SkillImpl (SS_KAGEGARI), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillShadowHunting;

impl SkillImpl for SkillShadowHunting {
    fn skill_id(&self) -> u16 {
        SS_KAGEGARI
    }

    /// SkillShadowHunting::applyAdditionalEffects: the skill's status (skill_get_time2).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillShadowHunting::calculateSkillRatio: 600 + 900% per level, + 5% x level per Shadow Flash level, + 5 x POW,
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 600 + 900 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, SS_KAGEGISSEN) * 5 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillShadowHunting::castendDamageId: the splash hit (flag & 1).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::splash_hit(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillShadowHunting::castendPos2: castendDamageId (flag | 1) on the enemies of the square around (x, y)
    /// (map_foreachinallarea).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::pos_splash(c, self.skill_id(), src, x, y, skill_lv, tick, *flag);
    }
}
