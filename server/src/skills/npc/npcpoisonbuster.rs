//! skills/npc/npcpoisonbuster.cpp: class SkillNpcPoisonBuster : public SkillImpl (NPC_POISON_BUSTER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skill::clif::skill_fail;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_POISON;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNpcPoisonBuster;

impl SkillImpl for SkillNpcPoisonBuster {
    fn skill_id(&self) -> u16 {
        NPC_POISON_BUSTER
    }

    /// SkillNpcPoisonBuster::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 1500 * skill_lv as i32;
    }

    /// SkillNpcPoisonBuster::castendDamageId: hits a poisoned target and cures its poison; a player casting it on a
    /// target without poison gets the skill failure.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // status_change* tsc = status_get_sc(target); map_session_data* sd = BL_CAST(BL_PC, src)
        if sce(c, target, SC_POISON).is_some() {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
            sc::status_change_end(c, target.id, SC_POISON, 0);
        } else if src.kind == BL_PC {
            skill_fail(c, src, id);
        }
    }
}
