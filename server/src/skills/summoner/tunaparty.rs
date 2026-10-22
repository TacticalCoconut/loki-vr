//! skills/summoner/tunaparty.cpp: class SkillTunaParty : public SkillImpl (SU_TUNAPARTY).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillTunaParty;

impl SkillImpl for SkillTunaParty {
    fn skill_id(&self) -> u16 {
        SU_TUNAPARTY
    }

    /// SkillTunaParty::castendNoDamageId: the status, the packet from the target to itself.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let ok = sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&t), &t, id, skill_lv as i32, ok);
    }
}
