//! skills/archer/songofmana.cpp: class SkillSongOfMana : public SkillImpl (WM_SONG_OF_MANA).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSongOfMana;

impl SkillImpl for SkillSongOfMana {
    fn skill_id(&self) -> u16 {
        WM_SONG_OF_MANA
    }

    /// SkillSongOfMana::castendNoDamageId: the body songofmana.cpp, dancewithawarg.cpp, leradsdew.cpp, circleofnaturessound.cpp and unlimitedhummingvoice.cpp share (the caster starts the song with its Lesson level and, when that worked, the party members around it get it too; see skills/archer/mod.rs wm_party_song).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::wm_party_song(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }
}
