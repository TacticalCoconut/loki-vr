//! skills/taekwon/novaexplosion.cpp: class SkillNovaExplosion : public SkillImpl (SJ_NOVAEXPLOSING).

use crate::battle::BF_MISC;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_DIMENSION, SC_NOVAEXPLOSING};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNovaExplosion;

impl SkillImpl for SkillNovaExplosion {
    fn skill_id(&self) -> u16 {
        SJ_NOVAEXPLOSING
    }

    /// SkillNovaExplosion::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        skill_attack(c, BF_MISC, src, src, target, id, skill_lv, tick, *flag);

        let s = crate::unit::get_or_freed(c, src.id).unwrap_or_else(|| src.clone());
        // We can end Dimension here since the cooldown code is processed before this point.
        if sce(c, &s, SC_DIMENSION).is_some() {
            sc::status_change_end(c, s.id, SC_DIMENSION, 0);
        } else if s.kind == BL_PC {
            // Dimension not active? Activate the 2 second skill block penalty.
            // sc_start(src, sd, …): the target is the caster as a map_session_data.
            sc::sc_start(c, Some(&s), &s, SC_NOVAEXPLOSING, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        }
        // else: sd is nullptr for a caster that is not a player, and status_change_start's nullpo_ret(bl) returns
        // before doing anything: a monster caster gets no penalty.
    }
}
