//! skills/thief/sonicblow.cpp: class SkillSonicBlow : public WeaponSkillImpl (AS_SONICBLOW).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::npc::MF_BATTLEGROUND;
use crate::skills::novice::{pc_checkskill, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_SPIRIT, SC_STUN};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSonicBlow;

impl SkillImpl for SkillSonicBlow {
    fn skill_id(&self) -> u16 {
        AS_SONICBLOW
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSonicBlow::calculateSkillRatio (RENEWAL): +50% against a target under half HP.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, _src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let tstatus = crate::status::status_get_status_data(c, target);
        *base_skillratio += 100 + 100 * skill_lv as i32;
        if tstatus.hp < tstatus.max_hp / 2 {
            *base_skillratio += *base_skillratio / 2;
        }
    }

    /// SkillSonicBlow::applyAdditionalEffects: the Assassin soul link gives double stun chance outside GvG / BG.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        let linked = sce(c, src, SC_SPIRIT).map(|e| e.val2 == SL_ASSASIN as i32).unwrap_or(false);
        if !crate::status::map_flag_gvg2(c, target.map) && crate::npc::mapflag_value(c, target.map, MF_BATTLEGROUND) == 0 && linked {
            sc::sc_start(c, Some(src), target, SC_STUN, 4 * lv + 20, lv, skill_get_time2(id, skill_lv) as i64); //Link gives double stun chance outside GVG/BG
        } else {
            sc::sc_start(c, Some(src), target, SC_STUN, 2 * lv + 10, lv, skill_get_time2(id, skill_lv) as i64);
        }
    }

    /// SkillSonicBlow::modifyHitRate (RENEWAL): +90% with Sonic Acceleration.
    fn modify_hit_rate(&self, c: &Ctx, hit_rate: &mut i16, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if pc_checkskill(c, src, AS_SONICACCEL) > 0 {
            let h = *hit_rate as i32;
            *hit_rate = (h + h * 90 / 100) as i16;
        }
    }
}
