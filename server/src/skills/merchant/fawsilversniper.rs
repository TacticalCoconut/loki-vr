//! skills/merchant/fawsilversniper.cpp: class SkillFawSilverSniper : public SkillImpl (NC_SILVERSNIPER).

use super::ids::MOBID_SILVERSNIPER;
use crate::db::skills::*;
use crate::mob::spawn::mob_once_spawn_sub;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillFawSilverSniper::SkillFawSilverSniper() : SkillImpl(NC_SILVERSNIPER).
pub struct SkillFawSilverSniper;

impl SkillImpl for SkillFawSilverSniper {
    fn skill_id(&self) -> u16 {
        NC_SILVERSNIPER
    }

    /// SkillFawSilverSniper::castendPos2: a Silver Sniper at the cell (spawned with AI_NONE, then special_state.ai
    /// AI_FAW), attached to the caster until skill_get_time ends it.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let name = super::status_get_name(c, src);
        let md = mob_once_spawn_sub(c, Some(src), src.map, x, y, Some(&name), MOBID_SILVERSNIPER, Some(""), crate::status::defs::SZ_SMALL as u8, crate::mob::AI_NONE);
        if let Some(md) = md {
            super::summon_attach_and_spawn(c, md, Some(src.id), crate::mob::AI_FAW as i32, c.tick() + skill_get_time(id, skill_lv) as Tick);
        }
    }
}
