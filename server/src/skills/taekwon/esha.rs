//! skills/taekwon/esha.cpp: class SkillEsha : public SkillImplRecursiveDamageSplash (SP_SHA).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL};
use crate::skills::novice::splash_damage_default;
use crate::skills::taekwon::allow_es_magic_pc;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_SP_SHA, SC_STUN, SC_USE_SKILL_SP_SHA};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEsha;

impl SkillImpl for SkillEsha {
    fn skill_id(&self) -> u16 {
        SP_SHA
    }

    crate::splash_skill_defaults!();

    /// SkillEsha::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_SP_SHA, 100, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillEsha::applyCounterAdditionalEffects.
    fn apply_counter_additional_effects(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: &mut i32) {
        sc::sc_start(c, Some(src), src, SC_USE_SKILL_SP_SHA, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillEsha::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 5 * skill_lv as i32;
    }
}

impl RecursiveDamageSplash for SkillEsha {
    /// SkillEsha::splashDamage.
    fn splash_damage(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
        // If a enemy player is standing next to a mob when splash Es- skill is casted, the player won't get hurt.
        if !allow_es_magic_pc() && target.kind != BL_MOB {
            return 0;
        }
        splash_damage_default(c, self.skill_id(), src, target, skill_lv, tick, flag)
    }

    /// SkillEsha::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        if src.kind == BL_PC && !allow_es_magic_pc() && target.kind != BL_MOB {
            // (the stun goes on the target, with the raw flag 10 = SCSTART_NOTICKDEF | SCSTART_NORATEDEF, as rAthena does)
            sc::status_change_start(c, Some(src), target, SC_STUN, 10000, skill_lv as i32, 0, 0, 0, 500, 10, 0);
            clif_skill_fail(c, src, self.skill_id(), USESKILL_FAIL, 0, 0);
            return;
        }
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
