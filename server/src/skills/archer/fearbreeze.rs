//! skills/archer/fearbreeze.cpp: class SkillFearBreeze : public StatusSkillImpl (RA_FEARBREEZE).

use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::{status_castend_nodamage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFearBreeze;

impl SkillImpl for SkillFearBreeze {
    fn skill_id(&self) -> u16 {
        RA_FEARBREEZE
    }

    /// SkillFearBreeze::castendNoDamageId: the caster's damage packet (DMGVAL_IGNORE), then
    /// StatusSkillImpl::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_damage(c, src, src, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        status_castend_nodamage_id(c, id, false, src, target, skill_lv);
    }
}
