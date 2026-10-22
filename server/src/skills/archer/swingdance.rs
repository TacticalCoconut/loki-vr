//! skills/archer/swingdance.cpp: class SkillSwingDance : public SkillImpl (WA_SWING_DANCE).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSwingDance;

impl SkillImpl for SkillSwingDance {
    fn skill_id(&self) -> u16 {
        WA_SWING_DANCE
    }

    /// SkillSwingDance::castendNoDamageId: the body echosong.cpp, windmillrushattack.cpp, moonlitserenade.cpp, swingdance.cpp and symphonyoflovers.cpp share (the target alone without a party or on the recursive call, else the party members around the caster then the target; see skills/archer/mod.rs lesson_party_song).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::lesson_party_song(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
