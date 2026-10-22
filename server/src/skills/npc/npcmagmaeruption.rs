//! skills/npc/npcmagmaeruption.cpp: class SkillNpcMagmaEruption : public WeaponSkillImpl (NPC_MAGMA_ERUPTION),
//! rAthena e985006. The eruption is its skill unit, placed by a timerskill (skill/timerskill.rs → skill_unitsetting:
//! skillunit agent).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skill::SD_ANIMATION;
use crate::skills::novice::status_get_amotion;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillNpcMagmaEruption;

impl SkillImpl for SkillNpcMagmaEruption {
    fn skill_id(&self) -> u16 {
        NPC_MAGMA_ERUPTION
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillNpcMagmaEruption::applyAdditionalEffects: stun effect from 'slam'.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 90, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillNpcMagmaEruption::castendPos2: 1st, AoE 'slam' damage; 2nd, AoE 'eruption' unit (a timerskill after two
    /// attack motions).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | SD_ANIMATION | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::Config, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_addtimerskill(c, &s, tick + status_get_amotion(c, &s) as Tick * 2, 0, x, y, id, skill_lv, 0, *flag);
    }
}
