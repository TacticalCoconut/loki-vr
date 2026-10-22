//! skills/archer/soundblend.cpp: class SkillSoundBlend : public SkillImpl (TR_SOUNDBLEND).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_MYSTIC_SYMPHONY;
use crate::status::defs::{RC_DEMIHUMAN, RC_FISH};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoundBlend;

impl SkillImpl for SkillSoundBlend {
    fn skill_id(&self) -> u16 {
        TR_SOUNDBLEND
    }

    /// SkillSoundBlend::castendNoDamageId: the hit (skill_castend_damage_id, flag 0), then SC_SOUNDBLEND (val2 the
    /// caster) with its no-damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, 0);
        let s = super::fresh(c, src);
        let t = super::fresh(c, target);
        let ok = sc::sc_start2(c, Some(&s), &t, skill_get_sc(id) as i32, 100, skill_lv as i32, src.id as i32, skill_get_time(id, skill_lv) as i64);
        clif_skill_nodamage(c, Some(&s), &super::fresh(c, target), id, skill_lv as i32, ok);
    }

    /// SkillSoundBlend::castendDamageId: the no-damage packet, then skill_attack of the skill's type.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, &super::fresh(c, target), id, skill_lv, tick, *flag);
    }

    /// SkillSoundBlend::calculateSkillRatio: -100 + 120 * lv + 5 * SPL, RE_LVL_DMOD(100), doubled under Mystic
    /// Symphony (+ 50% more against fish and demi-humans).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        *skillratio += -100 + 120 * skill_lv as i32 + 5 * sstatus.spl as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_MYSTIC_SYMPHONY).is_some() {
            *skillratio += *skillratio * 100 / 100;
            if tstatus.race as i32 == RC_FISH || tstatus.race as i32 == RC_DEMIHUMAN {
                *skillratio += *skillratio * 50 / 100;
            }
        }
    }

    /// SkillSoundBlend::modifyElement: a player's arrow element.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        super::arrow_element(c, src, element);
    }
}
