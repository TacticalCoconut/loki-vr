//! skills/archer/improvisedsong.cpp: class SkillImprovisedSong : public SkillImpl (WM_RANDOMIZESPELL).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillImprovisedSong;

impl SkillImpl for SkillImprovisedSong {
    fn skill_id(&self) -> u16 {
        WM_RANDOMIZESPELL
    }

    /// SkillImprovisedSong::castendNoDamageId: a (30 + 10 * lv)% chance to end the target's Minstrel / Wanderer songs
    /// (SC_BEYONDOFWARCRY is ended twice, as rAthena lists it twice), with the no-damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if ((c.rnd() % 100) as i32) < 30 + 10 * skill_lv as i32 {
            for t in [SC_SONGOFMANA, SC_DANCEWITHWUG, SC_LERADSDEW, SC_SATURDAYNIGHTFEVER, SC_BEYONDOFWARCRY, SC_MELODYOFSINK, SC_BEYONDOFWARCRY, SC_UNLIMITEDHUMMINGVOICE] {
                sc::status_change_end(c, target.id, t, 0);
            }
            clif_skill_nodamage(c, Some(src), &super::fresh(c, target), self.skill_id(), skill_lv as i32, true);
        }
    }
}
