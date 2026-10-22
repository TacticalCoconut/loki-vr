//! skills/summoner/blessingofmysticalcreatures.cpp: class SkillBlessingofMysticalCreatures : public SkillImpl
//! (SH_BLESSING_OF_MYSTICAL_CREATURES).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::hp::status_heal;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillBlessingofMysticalCreatures;

impl SkillImpl for SkillBlessingofMysticalCreatures {
    fn skill_id(&self) -> u16 {
        SH_BLESSING_OF_MYSTICAL_CREATURES
    }

    /// SkillBlessingofMysticalCreatures::castendNoDamageId: `status_heal(target, 0, 0, 200-status_get_ap(target), 0)`
    /// (uint32 AP: above 200 the difference wraps to a huge heal, as in rAthena), the status, and the packet on the
    /// caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let ap = crate::status::status_get_status_data(c, target).ap;
        status_heal(c, target.id, 0, 0, 200u32.wrapping_sub(ap) as i64, 0);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::sc_start(c, Some(src), &t, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
    }
}
