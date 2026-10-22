//! skills/acolyte/kiexplosion.cpp: class SkillKiExplosion : public SkillImpl (MO_BALKYOUNG).

use crate::battle::calc::Damage;
use crate::battle::{ATK_DEF, BCT_ENEMY, BF_MISC, BF_WEAPON};
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::{skill_attack, skill_blown, BLOWN_NONE};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::effect::skill_additional_effect;
use crate::skills::SkillImpl;
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillKiExplosion;

impl SkillImpl for SkillKiExplosion {
    fn skill_id(&self) -> u16 {
        MO_BALKYOUNG
    }

    /// SkillKiExplosion::modifyDamageData: the hit itself pushes nothing back (the splash does).
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        dmg.blewcount = 0;
    }

    /// SkillKiExplosion::castendNoDamageId: passive part of the attack. Splash knock-back + stun. [Skotlex]
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if area::temp(1) != target.id as i32 {
            skill_blown(c, src, target, skill_get_blewcount(id, skill_lv), -1, BLOWN_NONE);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            // Use Misc rather than weapon to signal passive pushback
            skill_additional_effect(c, &s, &t, id, skill_lv, BF_MISC, ATK_DEF, tick);
        }
    }

    /// SkillKiExplosion::castendDamageId: active part of the attack. Skill-attack [Skotlex]; a hit sends the splash
    /// (skill_castend_nodamage_id on everyone around the target but the target).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        area::set_temp(1, target.id as i32); // NOTE: This is used in skill_castend_nodamage_id to avoid affecting the target.
        if skill_attack(c, BF_WEAPON, src, src, target, id, skill_lv, tick, *flag) != 0 {
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, &t, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        }
    }

    /// SkillKiExplosion::calculateSkillRatio: RENEWAL +700%.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 700;
    }

    /// SkillKiExplosion::applyAdditionalEffects: attack_type is BF_WEAPON for the actual target, BF_MISC for the
    /// splash-affected mobs; 70% base stun chance on those.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, attack_type: i32, _dmg_lv: u8) {
        if attack_type & BF_MISC != 0 {
            sc::sc_start(c, Some(src), target, SC_STUN, 70, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
        }
    }
}
