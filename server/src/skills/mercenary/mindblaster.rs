//! skills/mercenary/mercenary_mindblaster.cpp: class SkillMercenaryMindBlaster : public SkillImpl (MER_INVINCIBLEOFF2).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::SC_INVINCIBLE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryMindBlaster;

impl SkillImpl for SkillMercenaryMindBlaster {
    fn skill_id(&self) -> u16 {
        MER_INVINCIBLEOFF2
    }

    /// SkillMercenaryMindBlaster::castendNoDamageId: the packet first, then SC_INVINCIBLE ends.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        sc::status_change_end(c, target.id, SC_INVINCIBLE, 0);
    }
}
