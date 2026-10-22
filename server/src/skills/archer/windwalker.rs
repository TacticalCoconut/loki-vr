//! skills/archer/windwalker.cpp: class SkillWindWalker : public SkillImpl (SN_WINDWALK).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWindWalker;

impl SkillImpl for SkillWindWalker {
    fn skill_id(&self) -> u16 {
        SN_WINDWALK
    }

    /// SkillWindWalker::castendNoDamageId: the target alone (the packet from the target itself) without a party or on
    /// the recursive call, else the party members around the caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if super::no_party(c, src) || *flag & 1 != 0 {
            let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let t = super::fresh(c, target);
            clif_skill_nodamage(c, Some(&t), &t, id, skill_lv as i32, ok);
        } else {
            party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, *flag | BCT_PARTY | 1, skill_castend_nodamage_id);
        }
    }
}
