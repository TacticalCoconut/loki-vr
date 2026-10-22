//! skills/gunslinger/richscoin.cpp: class SkillRichsCoin : public SkillImpl (RL_RICHS_COIN), rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state::pc_addspiritball;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRichsCoin;

impl SkillImpl for SkillRichsCoin {
    fn skill_id(&self) -> u16 {
        RL_RICHS_COIN
    }

    /// SkillRichsCoin::castendNoDamageId: a player gets 10 coins (pc_addspiritball 10 times, max 10).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        for _ in 0..10 {
            pc_addspiritball(c, src.id, skill_get_time(id, skill_lv), 10);
        }
    }
}
