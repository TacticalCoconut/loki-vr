//! skills/thief/poisoningweapon.cpp: class SkillPoisoningWeapon : public SkillImpl (GC_POISONINGWEAPON).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillPoisoningWeapon;

impl SkillImpl for SkillPoisoningWeapon {
    fn skill_id(&self) -> u16 {
        GC_POISONINGWEAPON
    }

    /// SkillPoisoningWeapon::castendNoDamageId: the poison list (the choice comes back through
    /// clif_parse_SelectArrow → skill_poisoningweapon), then the animation.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        super::ext::clif_poison_list(c, src, skill_lv);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, self.skill_id(), skill_lv as i32, true);
    }
}
