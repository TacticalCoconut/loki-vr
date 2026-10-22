//! skills/elemental/waterscreen.cpp: class SkillWaterScreen : public SkillImpl (EL_WATER_SCREEN), rAthena e985006.
//! Unlike the other option skills (super::option_castend_nodamage_id) its animation comes first, a running pair is
//! ended where it is (the option on the target, SC_WATER_SCREEN on the elemental), and the option's val1 is the
//! elemental's id (battle_delay_damage / battle_weapon_attack / skill_attack pass the damage to it).

use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::map::BL_ELEM;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::{sce, status_get_amotion};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillWaterScreen;

impl SkillImpl for SkillWaterScreen {
    fn skill_id(&self) -> u16 {
        EL_WATER_SCREEN
    }

    /// SkillWaterScreen::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // BL_CAST(BL_ELEM, src)
        if src.kind != BL_ELEM {
            return;
        }
        let type2 = type_ - 1;
        clif_skill_nodamage(c, Some(src), src, id, skill_lv as i32, true);
        if sce(c, src, type2).is_some() || sce(c, target, type_).is_some() {
            sc::status_change_end(c, target.id, type_, 0);
            sc::status_change_end(c, src.id, type2, 0);
        } else {
            // This not heals at the end.
            clif_skill_damage(c, src, src, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
            let dur = skill_get_time(id, skill_lv) as i64;
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            sc::sc_start(c, Some(&s), &s, type2, 100, skill_lv as i32, dur);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            // val1 = src->id
            sc::sc_start(c, Some(&s), &t, type_, 100, src.id as i32, dur);
        }
    }
}
