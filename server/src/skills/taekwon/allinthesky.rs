//! skills/taekwon/allinthesky.cpp: class SkillAllInTheSky : public SkillImpl (SKE_ALL_IN_THE_SKY).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::status::defs::{RC_DEMIHUMAN, RC_DEMON};
use crate::status::hp::status_zap;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAllInTheSky;

impl SkillImpl for SkillAllInTheSky {
    fn skill_id(&self) -> u16 {
        SKE_ALL_IN_THE_SKY
    }

    /// SkillAllInTheSky::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if target.kind == BL_PC {
            // status_get_ap(target): status_get_status_data(*target)->ap
            let ap = crate::status::status_get_status_data(c, target).ap;
            status_zap(c, target.id, 0, 0, ap as i64);
        }
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        if unit::movepos(c, src.id, t.x, t.y, 2, true) {
            // clif_snap(src, src->x, src->y): the caster as it is after the move (skill/clif.rs, the acolyte agent's
            // port, taken identically)
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::skill::clif::clif_snap(c, &s, s.x, s.y);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or(t);
        skill_attack(c, skill_get_type(id), &s, &s, &t, id, skill_lv, tick, *flag);
    }

    /// SkillAllInTheSky::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 250 + 1200 * skill_lv as i32;
        *base_skillratio += 5 * sstatus.pow as i32;
    }

    /// SkillAllInTheSky::modifyDamageData.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, _src: &Unit, target: &Unit, _skill_lv: u16) {
        // status_get_race(&target): status_get_status_data(*target)->race
        match crate::status::status_get_status_data(c, target).race as i32 {
            RC_DEMIHUMAN | RC_DEMON => dmg.div_ = 3,
            _ => {}
        }
    }
}
