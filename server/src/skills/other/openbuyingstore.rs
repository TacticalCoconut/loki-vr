//! skills/other/openbuyingstore.cpp: class SkillOpenBuyingStore : public SkillImpl (ALL_BUYING_STORE).

use crate::buyingstore::MAX_BUYINGSTORE_SLOTS;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillOpenBuyingStore;

impl SkillImpl for SkillOpenBuyingStore {
    fn skill_id(&self) -> u16 {
        ALL_BUYING_STORE
    }

    /// SkillOpenBuyingStore::castendNoDamageId: players only, the skill allows 5 buying slots (buyingstore_setup runs
    /// first; the packet's result is whether it answered 0).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        let ok = crate::buyingstore::buyingstore_setup(c, src.id, MAX_BUYINGSTORE_SLOTS as u8) == 0;
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, self.skill_id(), skill_lv as i32, ok);
    }
}
