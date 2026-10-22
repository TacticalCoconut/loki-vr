//! skills/mage/readingspellbook.cpp: class SkillReadingSpellbook : public SkillImpl (WL_READING_SB_READING), rAthena
//! e985006: the skill the client uses when a Warlock picks a book of the Magic Book list (skill_lv = the book, 1..10);
//! the spell is sealed by skill.cpp skill_spellbook (skill/misc.rs).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, fail::USESKILL_FAIL_SPELLBOOK_READING, skill_fail_cause};
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// itemdb.hpp ITEMID_WL_MB_SG (Magic Book (Storm Gust), the first book of the Reading Spellbook list).
pub const ITEMID_WL_MB_SG: u32 = 100065;

pub struct SkillReadingSpellbook;

impl SkillImpl for SkillReadingSpellbook {
    fn skill_id(&self) -> u16 {
        WL_READING_SB_READING
    }

    /// SkillReadingSpellbook::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            let id = self.skill_id();
            if pc_checkskill(c, src, WL_READING_SB) == 0 || skill_lv < 1 || skill_lv > 10 {
                skill_fail_cause(c, src, id, USESKILL_FAIL_SPELLBOOK_READING);
                return;
            }
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            crate::skill::misc::skill_spellbook(c, src, ITEMID_WL_MB_SG + skill_lv as u32 - 1);
        }
    }
}
