//! skills/thief/venomsplasher.cpp: class SkillVenomSplasher : public SkillImplRecursiveDamageSplash (AS_SPLASHER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::pc_checkskill;
use crate::skills::{splash_castend_damage_id, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_POISON;
use crate::status::defs::MD_STATUSIMMUNE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillVenomSplasher;

impl SkillImpl for SkillVenomSplasher {
    fn skill_id(&self) -> u16 {
        AS_SPLASHER
    }

    /// SkillVenomSplasher::calculateSkillRatio (RENEWAL): + 20% per Poison React level.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 400 + 100 * skill_lv as i32;
        if src.kind == BL_PC {
            *base_skillratio += 20 * pc_checkskill(c, src, AS_POISONREACT);
        }
    }

    /// SkillVenomSplasher::castendNoDamageId: not on a status-immune target (Renewal dropped the 3/4 hp requirement);
    /// SC_SPLASHER (val2 = the skill, val3 = the caster, val4 = the explosion delay 1000 ms: the status timer then
    /// calls the damage part), the animation with its result.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        let type_ = skill_get_sc(id) as i32;
        if tstatus.mode & MD_STATUSIMMUNE != 0 {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            return;
        }
        let ok = sc::sc_start4(c, Some(src), target, type_, 100, skill_lv as i32, id as i32, src.id as i32, skill_get_time(id, skill_lv), 1000);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, ok);
    }

    /// SkillVenomSplasher::castendDamageId: the recursive splash; the first call does not consume a second gemstone.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
        if *flag & 1 == 0 {
            // Don't consume a second gemstone.
            *flag |= SKILL_NOCONSUME_REQ;
        }
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (not overridden).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }

    /// SkillVenomSplasher::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start2(c, Some(src), target, SC_POISON, 100, skill_lv as i32, src.id as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}

impl RecursiveDamageSplash for SkillVenomSplasher {
    /// SkillVenomSplasher::getSearchSize: Venom Splasher uses a different range for searching than for splashing.
    fn get_search_size(&self, _c: &Ctx, _src: &Unit, _skill_lv: u16) -> i16 {
        1
    }
}
