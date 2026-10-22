//! skills/archer/pangvoice.cpp: class SkillPangVoice : public SkillImpl (BA_PANGVOICE).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLEEDING, SC_CONFUSION};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPangVoice;

impl SkillImpl for SkillPangVoice {
    fn skill_id(&self) -> u16 {
        BA_PANGVOICE
    }

    /// SkillPangVoice::castendNoDamageId: "In Renewal it causes Confusion and Bleeding to 100% base chance", then the
    /// no-damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, SC_CONFUSION, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        sc::sc_start(c, Some(src), &super::fresh(c, target), SC_BLEEDING, 100, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        clif_skill_nodamage(c, Some(src), &super::fresh(c, target), id, skill_lv as i32, true);
    }
}
