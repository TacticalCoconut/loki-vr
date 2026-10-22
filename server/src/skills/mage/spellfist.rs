//! skills/mage/spellfist.cpp: class SkillSpellFist : public SkillImpl (SO_SPELLFIST).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state;
use crate::skill::unit::unit_skillcastcancel;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSpellFist;

impl SkillImpl for SkillSpellFist {
    fn skill_id(&self) -> u16 {
        SO_SPELLFIST
    }

    /// SkillSpellFist::castendNoDamageId: the packet, cancels the cast of the bolt it was cast over
    /// (unit_skillcastcancel type 1: the skill in sd->skill_id_old), then SC_SPELLFIST (val1 = lv, val2 / val3 =
    /// the bolt's skill and level) when a skill is remembered; sd->skill_id_old / skill_lv_old are reset.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();

        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        unit_skillcastcancel(c, src.id, 1);

        if src.kind == BL_PC {
            let st = state::get(c, src.id);
            if st.skill_id_old != 0 && st.skill_lv_old != 0 {
                let src = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                sc::sc_start4(
                    c,
                    Some(&src),
                    &src,
                    skill_get_sc(id) as i32,
                    100,
                    skill_lv as i32,
                    st.skill_id_old as i32,
                    st.skill_lv_old as i32,
                    0,
                    skill_get_time(id, skill_lv) as i64,
                );
            }
            state::with(c, src.id, |s| {
                s.skill_id_old = 0;
                s.skill_lv_old = 0;
            });
        }
    }
}
