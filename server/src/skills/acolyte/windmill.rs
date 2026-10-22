//! skills/acolyte/windmill.cpp: class SkillWindmill : public SkillImplRecursiveDamageSplash (SR_WINDMILL).

use crate::battle::calc::Damage;
use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::{re_lvl_dmod, status_get_amotion};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_STUN;
use crate::status::{sc, status_get_lv};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillWindmill;

impl SkillImpl for SkillWindmill {
    fn skill_id(&self) -> u16 {
        SR_WINDMILL
    }

    crate::splash_skill_defaults!();

    /// SkillWindmill::applyAdditionalEffects: a player is knocked down by a timerskill after the caster's amotion; a
    /// monster is stunned for 1-3 s.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        if target.kind == BL_PC {
            skill_addtimerskill(c, src, tick + status_get_amotion(c, src) as i64, target.id, 0, 0, self.skill_id(), skill_lv, BF_WEAPON, 0);
        } else if target.kind == BL_MOB {
            let duration = 1000 + 1000 * (c.rnd() % 3) as i64;
            sc::sc_start(c, Some(src), target, SC_STUN, 100, skill_lv as i32, duration);
        }
    }

    /// SkillWindmill::calculateSkillRatio: ATK [(Caster Base Level + Caster DEX) x Caster Base Level / 100] %.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + status_get_lv(c, src) + sstatus.dex as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillWindmill::castendNoDamageId: the animation, then skill_castend_damage_id on the caster (the splash
    /// around it).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_castend_damage_id(c, &s, &s, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillWindmill {}
