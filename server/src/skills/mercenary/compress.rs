//! skills/mercenary/mercenary_compress.cpp: class SkillMercenaryCompress : public SkillImpl (MER_COMPRESS).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::SC_BLEEDING;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryCompress;

impl SkillImpl for SkillMercenaryCompress {
    fn skill_id(&self) -> u16 {
        MER_COMPRESS
    }

    /// SkillMercenaryCompress::castendNoDamageId: cures Bleeding.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        sc::status_change_end(c, target.id, SC_BLEEDING, 0);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, self.skill_id(), skill_lv as i32, true);
    }
}
