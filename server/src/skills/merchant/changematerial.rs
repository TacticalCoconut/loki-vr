//! skills/merchant/changematerial.cpp: class SkillChangeMaterial : public SkillImpl (GN_CHANGEMATERIAL). Its window
//! (clif_skill_itemlistwindow) is skill/clif.rs's, the answer (clif_parse_ItemListWindowSelected) skill/parse.rs's and
//! skill_changematerial itemui/produce.rs's.

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillChangeMaterial::SkillChangeMaterial() : SkillImpl(GN_CHANGEMATERIAL).
pub struct SkillChangeMaterial;

impl SkillImpl for SkillChangeMaterial {
    fn skill_id(&self) -> u16 {
        GN_CHANGEMATERIAL
    }

    /// SkillChangeMaterial::castendNoDamageId: the skill packet, then the item list window (players).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            crate::skill::clif::clif_skill_itemlistwindow(c, src, id, skill_lv);
        }
    }
}
