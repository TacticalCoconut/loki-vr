//! skills/archer/unbarringoctave.cpp: class SkillUnbarringOctave : public SkillImpl (BA_FROSTJOKER).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::defs::SCSTART_NONE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillUnbarringOctave;

impl SkillImpl for SkillUnbarringOctave {
    fn skill_id(&self) -> u16 {
        BA_FROSTJOKER
    }

    /// SkillUnbarringOctave::applyAdditionalEffects: the skill's status at 150 + 50 * lv (Aegis accuracy, 1000 =
    /// 100%) for skill_get_time2; on party members the chance is divided by 4 and the duration is fixed to
    /// skill_get_time (15000 ms).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        let mut rate = 150 + 50 * skill_lv as i32; // Aegis accuracy (1000 = 100%)
        let mut duration = skill_get_time2(id, skill_lv);
        if crate::battle::target::battle_check_target(c, src, target, BCT_PARTY) > 0 {
            // On party members: Chance is divided by 4 and duration is fixed to 15000ms
            rate /= 4;
            duration = skill_get_time(id, skill_lv);
        }
        sc::status_change_start(c, Some(src), target, skill_get_sc(id) as i32, rate * 10, skill_lv as i32, 0, 0, 0, duration as i64, SCSTART_NONE, 0);
    }

    /// SkillUnbarringOctave::castendNoDamageId: the no-damage packet and a timer skill 3 s later at the caster's
    /// cell (skill_timerskill -> skill_frostjoke_scream on everyone around it); a monster shouts the skill's name.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = super::fresh(c, src);
        crate::skill::timerskill::skill_addtimerskill(c, &s, tick + 3000, target.id, s.x, s.y, id, skill_lv, 0, *flag);
        super::mob_skill_overhead(c, &s, id);
    }
}
