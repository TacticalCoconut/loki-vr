//! skills/acolyte/zen.cpp: class SkillZen : public SkillImpl (CH_SOULCOLLECT).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state::pc_addspiritball;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_RAISINGDRAGON;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillZen;

impl SkillImpl for SkillZen {
    fn skill_id(&self) -> u16 {
        CH_SOULCOLLECT
    }

    /// SkillZen::castendNoDamageId: fills the spheres up to 5 (+ Raising Dragon's level).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let mut limit = 5;
        if let Some(e) = sce(c, src, SC_RAISINGDRAGON) {
            limit += e.val1;
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        for _ in 0..limit {
            pc_addspiritball(c, src.id, skill_get_time(id, skill_lv), limit);
        }
    }
}
