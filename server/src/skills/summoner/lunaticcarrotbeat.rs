//! skills/summoner/lunaticcarrotbeat.cpp: class SkillLunaticCarrotBeat : public SkillImplRecursiveDamageSplash
//! (SU_LUNATICCARROTBEAT) and class SkillLunaticCarrotBeat2 : public SkillImplRecursiveDamageSplash
//! (SU_LUNATICCARROTBEAT2).

use super::add_hp_ratio;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{splash_castend_damage_id, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// The calculateSkillRatio both classes have (the same body).
fn carrot_ratio(c: &Ctx, src: &Unit, skill_lv: u16, skillratio: &mut i32) {
    let sstatus = crate::status::status_get_status_data(c, src);
    *skillratio += 100 + 100 * skill_lv as i32;
    if src.kind == BL_PC && pc_checkskill(c, src, SU_SPIRITOFLIFE) != 0 {
        add_hp_ratio(c, src, skillratio);
    }
    if crate::status::status_get_lv(c, src) > 99 {
        *skillratio += sstatus.str as i32;
    }
    re_lvl_dmod(c, src, skillratio, 100);
}

// SU_LUNATICCARROTBEAT
pub struct SkillLunaticCarrotBeat;

impl SkillImpl for SkillLunaticCarrotBeat {
    fn skill_id(&self) -> u16 {
        SU_LUNATICCARROTBEAT
    }

    /// SkillLunaticCarrotBeat::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        carrot_ratio(c, src, skill_lv, skillratio);
    }

    /// SkillLunaticCarrotBeat::castendDamageId: only the first call acts (the splash calls, flag & 1, do nothing): a
    /// player holding the skill's first required item runs SkillLunaticCarrotBeat2's castendDamageId ("FIX ME:
    /// missing check of required item": the skill has no ItemCost, itemid[0] is 0 and pc_search_inventory(sd, 0)
    /// answers the first empty inventory slot), everyone else the parent's (SkillImplRecursiveDamageSplash).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if *flag & 1 == 0 {
            let item = skill_db().find(self.skill_id()).map(|s| s.require.itemid[0]).unwrap_or(0);
            if src.kind == BL_PC && crate::inventory::pc_search_inventory(c, src.id, item) >= 0 {
                // SkillLunaticCarrotBeat2 lunatic2; lunatic2.castendDamageId(…)
                let lunatic2 = SkillLunaticCarrotBeat2;
                lunatic2.castend_damage_id(c, src, target, skill_lv, tick, flag);
            } else {
                splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
            }
        }
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (inherited).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }
}

impl RecursiveDamageSplash for SkillLunaticCarrotBeat {}

// SU_LUNATICCARROTBEAT2
pub struct SkillLunaticCarrotBeat2;

impl SkillImpl for SkillLunaticCarrotBeat2 {
    fn skill_id(&self) -> u16 {
        SU_LUNATICCARROTBEAT2
    }

    crate::splash_skill_defaults!();

    /// SkillLunaticCarrotBeat2::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        carrot_ratio(c, src, skill_lv, skillratio);
    }

    /// SkillLunaticCarrotBeat2::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 20, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}

impl RecursiveDamageSplash for SkillLunaticCarrotBeat2 {}
