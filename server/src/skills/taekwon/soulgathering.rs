//! skills/taekwon/soulgathering.cpp: class SkillSoulGathering : public SkillImpl (SOA_SOUL_GATHERING).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state::{pc_addsoulball, MAX_SOUL_BALL};
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSoulGathering;

impl SkillImpl for SkillSoulGathering {
    fn skill_id(&self) -> u16 {
        SOA_SOUL_GATHERING
    }

    /// SkillSoulGathering::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        // map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            let limit = 5 + pc_checkskill(c, src, SP_SOULENERGY) * 3;
            for _ in 0..limit {
                // pc_addsoulball(*sd, limit): `limit` is the number of souls added each time; max keeps its default
                // (MAX_SOUL_BALL)
                pc_addsoulball(c, src.id, limit, MAX_SOUL_BALL);
            }
        }
    }
}
