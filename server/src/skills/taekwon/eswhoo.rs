//! skills/taekwon/eswhoo.cpp: class SkillEswhoo : public SkillImplRecursiveDamageSplash (SP_SWHOO).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL};
use crate::skills::novice::{re_lvl_dmod, splash_damage_default};
use crate::skills::taekwon::allow_es_magic_pc;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_STUN, SC_USE_SKILL_SP_SHA, SC_USE_SKILL_SP_SPA};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEswhoo;

impl SkillImpl for SkillEswhoo {
    fn skill_id(&self) -> u16 {
        SP_SWHOO
    }

    crate::splash_skill_defaults!();

    /// SkillEswhoo::applyCounterAdditionalEffects.
    fn apply_counter_additional_effects(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: &mut i32) {
        // (SC_USE_SKILL_SP_SHA with Eswhoo's own skill_get_time, as rAthena does)
        sc::sc_start(c, Some(src), src, SC_USE_SKILL_SP_SHA, 100, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillEswhoo::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 1000 + 200 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillEswhoo {
    /// SkillEswhoo::splashDamage.
    fn splash_damage(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
        // If a enemy player is standing next to a mob when splash Es- skill is casted, the player won't get hurt.
        if !allow_es_magic_pc() && target.kind != BL_MOB {
            return 0;
        }
        splash_damage_default(c, self.skill_id(), src, target, skill_lv, tick, flag)
    }

    /// SkillEswhoo::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        if src.kind == BL_PC && !allow_es_magic_pc() && target.kind != BL_MOB {
            // (the stun goes on the target, with the raw flag 10 = SCSTART_NOTICKDEF | SCSTART_NORATEDEF, as rAthena does)
            sc::status_change_start(c, Some(src), target, SC_STUN, 10000, skill_lv as i32, 0, 0, 0, 500, 10, 0);
            clif_skill_fail(c, src, self.skill_id(), USESKILL_FAIL, 0, 0);
            return;
        }
        sc::status_change_end(c, src.id, SC_USE_SKILL_SP_SPA, 0);

        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
