//! skills/merchant/twilightalchemy3.cpp: class SkillTwilightAlchemy3 : public SkillImpl (AM_TWILIGHT3).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillTwilightAlchemy3::SkillTwilightAlchemy3() : SkillImpl(AM_TWILIGHT3).
pub struct SkillTwilightAlchemy3;

impl SkillImpl for SkillTwilightAlchemy3 {
    fn skill_id(&self) -> u16 {
        AM_TWILIGHT3
    }

    /// SkillTwilightAlchemy3::castendNoDamageId: fails unless the player can make 100 Alcohol, 50 Acid Bottles and 50
    /// Flame Bottles and has 200 Empty Bottles, then produces the three (skill_produce_mix with the produce_db
    /// indexes skill_can_produce_mix found, minus one).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        use super::ids::*;
        use crate::itemui::produce::{skill_can_produce_mix, skill_produce_mix};
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let aid = src.id;
        let mut ebottle = crate::inventory::pc_search_inventory(c, aid, ITEMID_EMPTY_BOTTLE) as i32;
        if ebottle >= 0 {
            ebottle = crate::inventory::inv_get(c, aid, ebottle as usize).map(|s| s.item.amount as i32).unwrap_or(0);
        }
        //check if you can produce all three, if not, then fail:
        // (short-circuit: the later indexes are only searched when the earlier ones were found)
        let (mut acid_idx, mut fire_idx) = (-1i16, -1i16);
        let alcohol_idx = skill_can_produce_mix(c, aid, ITEMID_ALCOHOL, -1, 100); //100 Alcohol
        let fails = {
            alcohol_idx == 0 || {
                acid_idx = skill_can_produce_mix(c, aid, ITEMID_ACID_BOTTLE, -1, 50); //50 Acid Bottle
                acid_idx == 0
            } || {
                fire_idx = skill_can_produce_mix(c, aid, ITEMID_FIRE_BOTTLE, -1, 50); //50 Flame Bottle
                fire_idx == 0
            } || ebottle < 200 //200 empty bottle are required at total.
        };
        if fails {
            skill_fail(c, src, id);
            return;
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_produce_mix(c, aid, id, ITEMID_ALCOHOL, 0, 0, 0, 100, alcohol_idx - 1);
        skill_produce_mix(c, aid, id, ITEMID_ACID_BOTTLE, 0, 0, 0, 50, acid_idx - 1);
        skill_produce_mix(c, aid, id, ITEMID_FIRE_BOTTLE, 0, 0, 0, 50, fire_idx - 1);
    }
}
