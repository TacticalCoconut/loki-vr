//! skills/merchant/bionicpharmacy.cpp: class SkillBionicPharmacy : public SkillImpl (BO_BIONIC_PHARMACY).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillBionicPharmacy::SkillBionicPharmacy() : SkillImpl(BO_BIONIC_PHARMACY).
pub struct SkillBionicPharmacy;

impl SkillImpl for SkillBionicPharmacy {
    fn skill_id(&self) -> u16 {
        BO_BIONIC_PHARMACY
    }

    /// SkillBionicPharmacy::castendNoDamageId: sd->skill_id_old / skill_lv_old, the cooking list (trigger 32, qty 1, list type 8), then the skill packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC {
            let qty: i32 = 1;
            crate::skill::state::with(c, src.id, |st| {
                st.skill_id_old = id;
                st.skill_lv_old = skill_lv;
            });
            crate::itemui::produce::clif_cooking_list(c, src.id, 32, id, qty, 8);
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        }
    }
}
