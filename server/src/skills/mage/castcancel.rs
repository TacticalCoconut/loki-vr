//! skills/mage/castcancel.cpp: class SkillCastCancel : public SkillImpl (SA_CASTCANCEL).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::unit::unit_skillcastcancel;
use crate::skills::SkillImpl;
use crate::status::hp::status_zap;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCastCancel;

impl SkillImpl for SkillCastCancel {
    fn skill_id(&self) -> u16 {
        SA_CASTCANCEL
    }

    /// SkillCastCancel::castendNoDamageId: cancels the caster's own cast (unit_skillcastcancel type 1: the skill of
    /// sd->skill_id_old) and a player loses 90% / 70% / 50% / 30% / 10% of that skill's SP.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        unit_skillcastcancel(c, src.id, 1);
        if src.kind == BL_PC {
            let st = crate::skill::state::get(c, src.id);
            let mut sp = skill_get_sp(st.skill_id_old, st.skill_lv_old);
            sp = sp.wrapping_mul(90 - (skill_lv as i32 - 1) * 20) / 100;
            if sp < 0 {
                sp = 0;
            }
            status_zap(c, src.id, 0, sp as i64, 0);
        }
    }
}
