//! skills/elemental/windstep.cpp: class SkillWindStep : public SkillImpl (EL_WIND_STEP), rAthena e985006. The option
//! skill of super::option_castend_nodamage_id with a push of the target between the packets and the status changes.

use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::map::BL_ELEM;
use crate::skill::attack::{skill_blown, BLOWN_NONE};
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::{sce, status_get_amotion};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillWindStep;

impl SkillImpl for SkillWindStep {
    fn skill_id(&self) -> u16 {
        EL_WIND_STEP
    }

    /// SkillWindStep::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // BL_CAST(BL_ELEM, src)
        if src.kind != BL_ELEM {
            return;
        }
        let type2 = type_ - 1;
        if sce(c, src, type2).is_some() || sce(c, target, type_).is_some() {
            // (as rAthena writes it: `type` ends on the elemental, `type2` on the target)
            sc::status_change_end(c, src.id, type_, 0);
            sc::status_change_end(c, target.id, type2, 0);
        } else {
            clif_skill_nodamage(c, Some(src), src, id, skill_lv as i32, true);
            clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
            // There aren't teleport, just push the master away.
            // skill_blown(src, target, (rnd()%skill_get_blewcount(id, lv))+1, rnd()%8, BLOWN_NONE): C++ leaves the
            // arguments' order open; the oracle's g++ build (map-server, SkillWindStep::castendNoDamageId+0x148)
            // draws the direction's rnd() first, then the count's. The count is passed as a char.
            let dir = (c.rnd() % 8) as i8;
            let r = c.rnd() as i32;
            let count = (r % skill_get_blewcount(id, skill_lv) + 1) as i8 as i32;
            skill_blown(c, src, target, count, dir, BLOWN_NONE);
            let dur = skill_get_time(id, skill_lv) as i64;
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            sc::sc_start(c, Some(&s), &s, type2, 100, skill_lv as i32, dur);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start(c, Some(&s), &t, type_, 100, skill_lv as i32, dur);
        }
    }
}
