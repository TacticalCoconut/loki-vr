//! skills/homunculus/homunculus_eternalquickcombo.cpp: class SkillEternalQuickCombo : public SkillImpl (MH_EQC).

use super::{fresh, hd_of, yommy_duration};
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_EQC, SC_STUN, SC_TINDER_BREAKER2};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEternalQuickCombo;

impl SkillImpl for SkillEternalQuickCombo {
    fn skill_id(&self) -> u16 {
        MH_EQC
    }

    /// SkillEternalQuickCombo::castendDamageId: SC_EQC on the target for the Yommy formula's duration (the skill
    /// packet with its result), then the hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let duration = yommy_duration(c, src, target, skill_lv); // Yommy formula
        let ok = sc::sc_start4(c, Some(src), target, SC_EQC, 100, skill_lv as i32, src.id as i32, 0, 0, duration as i64);
        let t = fresh(c, target);
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
        let (s, t) = (fresh(c, src), fresh(c, target));
        crate::skill::attack::skill_attack(c, skill_get_type(id), &s, &s, &t, id, skill_lv, tick, *flag);
    }

    /// SkillEternalQuickCombo::applyAdditionalEffects: a homunculus' hit stuns for 1000 * its level / 50 + 500 per
    /// skill level and ends the target's SC_TINDER_BREAKER2.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        if let Some(hd) = hd_of(c, src) {
            let dur = 1000 * hd.homunculus.level as i64 / 50 + 500 * skill_lv as i64;
            sc::sc_start2(c, Some(src), target, SC_STUN, 100, skill_lv as i32, target.id as i32, dur);
            sc::status_change_end(c, target.id, SC_TINDER_BREAKER2, 0);
        }
    }
}
