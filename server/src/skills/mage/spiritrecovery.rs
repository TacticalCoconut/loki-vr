//! skills/mage/spiritrecovery.cpp: class SkillSpiritRecovery : public SkillImpl (SO_EL_CURE). The elemental (sd->ed)
//! comes from elem_iface (mercelem agent).

use super::elem_iface;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSpiritRecovery;

impl SkillImpl for SkillSpiritRecovery {
    fn skill_id(&self) -> u16 {
        SO_EL_CURE
    }

    /// SkillSpiritRecovery::castendNoDamageId: 10% of the caster's HP / SP go to its elemental.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        let Some(ed) = elem_iface::sd_ed(c, src.id) else { return };
        let st = crate::status::status_get_status_data(c, src);
        // int32 s_hp = sd->battle_status.hp * 10 / 100 (uint32 arithmetic, then int32)
        let s_hp = (st.hp.wrapping_mul(10) / 100) as i32;
        let s_sp = (st.sp.wrapping_mul(10) / 100) as i32;
        if crate::status::hp::status_charge(c, src.id, s_hp as i64, s_sp as i64) == 0 {
            skill_fail(c, src, self.skill_id());
            return;
        }
        crate::status::hp::status_heal(c, ed, s_hp as i64, s_sp as i64, 0, 3);
        if let Some(ed_bl) = crate::unit::get(c, ed) {
            clif_skill_nodamage(c, Some(src), &ed_bl, self.skill_id(), skill_lv as i32, true);
        }
    }
}
