//! skills/acolyte/highnessheal.cpp: class SkillHighnessHeal : public SkillImpl (AB_HIGHNESSHEAL).

use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHighnessHeal;

impl SkillImpl for SkillHighnessHeal {
    fn skill_id(&self) -> u16 {
        AB_HIGHNESSHEAL
    }

    /// SkillHighnessHeal::castendDamageId: the offensive heal on an undead target (skill_castend_nodamage_id
    /// redirects it).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillHighnessHeal::castendNoDamageId: SkillHeal's code (heal.rs) except that `dstsd` starts as the target player
    /// (so healing another player can give heal_exp job exp) and Bite Scar is not ended.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        super::heal::heal_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, true);
    }
}
