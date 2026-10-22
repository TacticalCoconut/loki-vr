//! skills/gunslinger/glittering.cpp: class SkillGlittering : public SkillImpl (GS_GLITTERING), rAthena e985006.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state::{self as pcskill, pc_addspiritball, pc_delspiritball};
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGlittering;

impl SkillImpl for SkillGlittering {
    fn skill_id(&self) -> u16 {
        GS_GLITTERING
    }

    /// SkillGlittering::castendNoDamageId: a player flips a coin: with 20 + 10% per level it gains a coin (max 10);
    /// else it loses one, unless it knows Rich's Coin.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        if ((c.rnd() % 100) as i32) < 20 + 10 * skill_lv as i32 {
            pc_addspiritball(c, src.id, skill_get_time(id, skill_lv), 10);
        } else if pcskill::get(c, src.id).spiritball > 0 && pc_checkskill(c, src, RL_RICHS_COIN) == 0 {
            pc_delspiritball(c, src.id, 1, 0);
        }
    }
}
