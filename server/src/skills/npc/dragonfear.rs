//! skills/npc/dragonfear.cpp: class SkillDragonFear : public SkillImpl (NPC_DRAGONFEAR), rAthena e985006.

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_PREAMBLE;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLEEDING, SC_CONFUSION, SC_SILENCE, SC_STUN};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDragonFear;

impl SkillImpl for SkillDragonFear {
    fn skill_id(&self) -> u16 {
        NPC_DRAGONFEAR
    }

    /// SkillDragonFear::castendNoDamageId: every enemy around the target gets one of four statuses (flag & 1: one of
    /// them: a random one first, then the next ones in the list until one starts; the duration is skill_get_time2 at
    /// the status' place in the list, 1..4).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            const SC: [i32; 4] = [SC_STUN, SC_SILENCE, SC_CONFUSION, SC_BLEEDING];
            let mut i = (c.rnd() % SC.len() as u32) as usize;
            let j = i;
            loop {
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                if sc::sc_start2(c, Some(src), &t, SC[i], 100, skill_lv as i32, src.id as i32, skill_get_time2(id, (i + 1) as u16) as i64) {
                    break;
                }
                i += 1;
                if i == SC.len() {
                    i = 0;
                }
                if i == j {
                    break;
                }
            }
        } else {
            area::set_temp(2, 0); // For SD_PREAMBLE
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            let f = *flag | BCT_ENEMY | SD_PREAMBLE | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
        }
    }
}
