//! skills/archer/retrospection.cpp: class SkillRetrospection : public SkillImpl (TR_RETROSPECTION).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRetrospection;

impl SkillImpl for SkillRetrospection {
    fn skill_id(&self) -> u16 {
        TR_RETROSPECTION
    }

    /// SkillRetrospection::castendNoDamageId: the no-damage packet, then a player casts its last Trouvere song again
    /// (`unit_skilluse_id(src, src->id, sd->skill_id_song, sd->skill_lv_song)`; skill_get_requirement takes 30% off
    /// its SP).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if src.kind == crate::map::BL_PC {
            let s = crate::skill::state::get(c, src.id);
            crate::skill::unit::unit_skilluse_id(c, src.id, src.id, s.skill_id_song, s.skill_lv_song);
        }
    }
}
