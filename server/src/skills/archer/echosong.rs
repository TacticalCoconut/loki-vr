//! skills/archer/echosong.cpp: class SkillEchoSong : public SkillImpl (MI_ECHOSONG).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillEchoSong;

impl SkillImpl for SkillEchoSong {
    fn skill_id(&self) -> u16 {
        MI_ECHOSONG
    }

    /// SkillEchoSong::castendNoDamageId: the body echosong.cpp, windmillrushattack.cpp, moonlitserenade.cpp, swingdance.cpp and symphonyoflovers.cpp share (the target alone without a party or on the recursive call, else the party members around the caster then the target; see skills/archer/mod.rs lesson_party_song).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::lesson_party_song(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
