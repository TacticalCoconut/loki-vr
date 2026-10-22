//! skills/archer/pronmarch.cpp: class SkillPronMarch : public SkillImpl (TR_PRON_MARCH).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillPronMarch;

impl SkillImpl for SkillPronMarch {
    fn skill_id(&self) -> u16 {
        TR_PRON_MARCH
    }

    /// SkillPronMarch::castendNoDamageId: the body jawaiiserenade.cpp, musicalinterlude.cpp and pronmarch.cpp share (sc_start4 on the target without a party or on the recursive call, else the song is remembered for Retrospection, a partner doubles it (flag 2) and the party members around the caster get it; see skills/archer/mod.rs trouvere_party_song).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        super::trouvere_party_song(c, self.skill_id(), src, target, skill_lv, tick, flag);
    }
}
