//! skills/gunslinger/grenadefragment.cpp: class SkillGrenadeFragment : public SkillImpl (NW_GRENADE_FRAGMENT), rAthena
//! e985006.

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGrenadeFragment;

impl SkillImpl for SkillGrenadeFragment {
    fn skill_id(&self) -> u16 {
        NW_GRENADE_FRAGMENT
    }

    /// SkillGrenadeFragment::castendNoDamageId: levels 1-6 pick SC_GRENADE_FRAGMENT_<level> (the enum's consecutive
    /// values), level 7 ends all six; then the caster's animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        sc::status_change_end(c, src.id, skill_get_sc(id) as i32, 0);
        if skill_lv < 7 {
            let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start(c, Some(src), &t, SC_GRENADE_FRAGMENT_1 - 1 + skill_lv as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        } else if skill_lv == 7 {
            for t in [SC_GRENADE_FRAGMENT_1, SC_GRENADE_FRAGMENT_2, SC_GRENADE_FRAGMENT_3, SC_GRENADE_FRAGMENT_4, SC_GRENADE_FRAGMENT_5, SC_GRENADE_FRAGMENT_6] {
                sc::status_change_end(c, src.id, t, 0);
            }
        }
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
    }
}
