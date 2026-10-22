//! skills/gunslinger/quickdrawshot.cpp: class SkillQuickDrawShot : public SkillImpl (RL_QD_SHOT), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_C_MARKER, SC_QD_SHOT_READY};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillQuickDrawShot;

impl SkillImpl for SkillQuickDrawShot {
    fn skill_id(&self) -> u16 {
        RL_QD_SHOT
    }

    /// SkillQuickDrawShot::modifyDamageData: 1 hit, + 1 per 20 job levels of a player.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        dmg.div_ = 1;
        if src.kind == BL_PC {
            let job_level = crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32).unwrap_or(0);
            dmg.div_ += (job_level / 20) as i16;
        }
    }

    /// SkillQuickDrawShot::castendNoDamageId: the main target (skill_area_temp[1]) is always hit; castendDamageId
    /// (flag | 1) runs on every enemy around the caster; then SC_QD_SHOT_READY ends (to prevent spamming of the skill
    /// onto the target) and skill_area_temp[1] is cleared.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // Remember main target as it will always be hit by this skill
        area::set_temp(1, target.id as i32);
        // Iterate through all enemies in the area
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        // End here to prevent spamming of the skill onto the target
        sc::status_change_end(c, src.id, SC_QD_SHOT_READY, 0);
        area::set_temp(1, 0);
    }

    /// SkillQuickDrawShot::castendDamageId: except for main target, only units marked with crimson marker are valid
    /// targets.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if area::temp(1) == target.id as i32 || sce(c, target, SC_C_MARKER).is_some() {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
    }
}
