//! skills/merchant/aidberserkpotion.cpp: class SkillAidBerserkPotion : public SkillImpl (AM_BERSERKPITCHER). Its body is
//! aidpotion.cpp's with the target's level check and `if( hp > 0 )` for the heal packet: see
//! [`super::aidpotion::potion_pitcher`].

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillAidBerserkPotion::SkillAidBerserkPotion() : SkillImpl(AM_BERSERKPITCHER).
pub struct SkillAidBerserkPotion;

impl SkillImpl for SkillAidBerserkPotion {
    fn skill_id(&self) -> u16 {
        AM_BERSERKPITCHER
    }

    /// SkillAidBerserkPotion::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        super::aidpotion::potion_pitcher(c, self.skill_id(), true, src, target, skill_lv, flag);
    }
}
