//! skills/archer/skilledspecialsinger.cpp: class SkillSkilledSpecialSinger : public SkillImpl (CG_SPECIALSINGER).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_ENSEMBLEFATIGUE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSkilledSpecialSinger;

impl SkillImpl for SkillSkilledSpecialSinger {
    fn skill_id(&self) -> u16 {
        CG_SPECIALSINGER
    }

    /// SkillSkilledSpecialSinger::castendNoDamageId: the target's Ensemble Fatigue ends (with the no-damage packet
    /// first).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if sce(c, target, SC_ENSEMBLEFATIGUE).is_some() {
            clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
            sc::status_change_end(c, target.id, SC_ENSEMBLEFATIGUE, 0);
        }
    }
}
