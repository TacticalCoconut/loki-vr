//! skills/archer/roseblossom.cpp: class SkillRoseBlossom : public WeaponSkillImpl (TR_ROSEBLOSSOM) and class
//! SkillRoseBlossomAttack : public SkillImplRecursiveDamageSplash (TR_ROSEBLOSSOM_ATK: the bloom SC_ROSEBLOSSOM
//! casts).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_MYSTIC_SYMPHONY, SC_ROSEBLOSSOM, SC_SOUNDBLEND};
use crate::status::defs::{RC_DEMIHUMAN, RC_FISH};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRoseBlossom;

/// The end both classes' calculateSkillRatio share: + 3 * CON with Stage Manner, + 200 * lv on a Sound Blend target,
/// RE_LVL_DMOD(100), doubled under Mystic Symphony (+ 50% more against fish and demi-humans).
fn rose_ratio_tail(c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32) {
    let sstatus = crate::status::status_get_status_data(c, src);
    let tstatus = crate::status::status_get_status_data(c, target);
    if src.kind == crate::map::BL_PC && pc_checkskill(c, src, TR_STAGE_MANNER) > 0 {
        *skillratio += 3 * sstatus.con as i32;
    }
    if sce(c, target, SC_SOUNDBLEND).is_some() {
        *skillratio += 200 * skill_lv as i32;
    }
    re_lvl_dmod(c, src, skillratio, 100);
    if sce(c, src, SC_MYSTIC_SYMPHONY).is_some() {
        *skillratio *= 2;
        if tstatus.race as i32 == RC_FISH || tstatus.race as i32 == RC_DEMIHUMAN {
            *skillratio += *skillratio * 50 / 100;
        }
    }
}

impl SkillImpl for SkillRoseBlossom {
    fn skill_id(&self) -> u16 {
        TR_ROSEBLOSSOM
    }

    /// SkillRoseBlossom::castendDamageId: the no-damage packet, then WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        weapon_castend_damage_id(c, self.skill_id(), src, &super::fresh(c, target), skill_lv, tick, *flag);
    }

    /// SkillRoseBlossom::calculateSkillRatio: -100 + 200 + 2000 * lv, then the shared tail.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 200 + 2000 * skill_lv as i32;
        rose_ratio_tail(c, src, target, skill_lv, skillratio);
    }

    /// SkillRoseBlossom::applyAdditionalEffects: "Rose blossom seed can only bloom if the target is hit":
    /// SC_ROSEBLOSSOM (val2 TR_ROSEBLOSSOM_ATK, val3 the caster), then the target's Sound Blend ends.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start4(c, Some(src), target, SC_ROSEBLOSSOM, 100, skill_lv as i32, TR_ROSEBLOSSOM_ATK as i32, src.id as i32, 0, skill_get_time(id, skill_lv) as i64);
        sc::status_change_end(c, target.id, SC_SOUNDBLEND, 0);
    }
}

pub struct SkillRoseBlossomAttack;

impl SkillImpl for SkillRoseBlossomAttack {
    fn skill_id(&self) -> u16 {
        TR_ROSEBLOSSOM_ATK
    }

    crate::splash_skill_defaults!();

    /// SkillRoseBlossomAttack::calculateSkillRatio: -100 + 550 + 3850 * lv, then the shared tail.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 550 + 3850 * skill_lv as i32;
        rose_ratio_tail(c, src, target, skill_lv, skillratio);
    }
}

impl RecursiveDamageSplash for SkillRoseBlossomAttack {}
