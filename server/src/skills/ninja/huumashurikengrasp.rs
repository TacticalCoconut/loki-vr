//! skills/ninja/huumashurikengrasp.cpp: class SkillHuumaShurikenGrasp : public SkillImpl (SS_FUUMASHOUAKU), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHuumaShurikenGrasp;

impl SkillImpl for SkillHuumaShurikenGrasp {
    fn skill_id(&self) -> u16 {
        SS_FUUMASHOUAKU
    }

    /// SkillHuumaShurikenGrasp::applyAdditionalEffects: the skill's status (skill_get_time2).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillHuumaShurikenGrasp::calculateSkillRatio: 850 + 350% per level, + 5% x level per Huuma Shuriken Construct
    /// level, + 5 x POW, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 850 + 350 * skill_lv as i32;
        *skillratio += pc_checkskill(c, src, SS_FUUMAKOUCHIKU) * 5 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHuumaShurikenGrasp::castendPos2: the unit (ammo should be deleted right away: no `flag |= 1`).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillHuumaShurikenGrasp::modifyElement: a player's arrow (ammunition) element.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if let Some(e) = super::arrow_ele(c, src) {
            *element = e;
        }
    }
}
