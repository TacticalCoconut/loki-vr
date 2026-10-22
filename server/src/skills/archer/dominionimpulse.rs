//! skills/archer/dominionimpulse.cpp: class SkillDominionImpulse : public SkillImpl (WM_DOMINION_IMPULSE) and the
//! file's static skill_active_reverberation.

use crate::db::skills::*;
use crate::skill::area::{foreach_in_area, foreach_in_range, Wall};
use crate::skills::SkillImpl;
use crate::skillunit::consts::UNT_USED_TRAPS;
use crate::skillunit::data;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillDominionImpulse;

impl SkillImpl for SkillDominionImpulse {
    fn skill_id(&self) -> u16 {
        WM_DOMINION_IMPULSE
    }

    /// SkillDominionImpulse::castendPos2: every Reverberation unit in the area goes off.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let i = skill_get_splash(self.skill_id(), skill_lv) as i16;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, crate::map::BL_SKILL, Wall::None, skill_active_reverberation);
    }
}

/// dominionimpulse.cpp static skill_active_reverberation: a live NPC_REVERBERATION unit sets off its trap splash
/// around itself (map_foreachinallrange(skill_trap_splash, …, sg->bl_flag, bl, gettick())), ends now and becomes a
/// used trap. Returns 1 for every skill unit.
fn skill_active_reverberation(c: &Ctx, bl: &Unit) -> i32 {
    if bl.kind != crate::map::BL_SKILL {
        return 0;
    }
    let Some(su) = data::get_unit(c, bl.id) else { return 1 };
    let Some(sg) = data::skill_id2group(c, su.group_id) else { return 1 };
    if su.alive && sg.skill_id == NPC_REVERBERATION {
        let now = c.tick();
        foreach_in_range(c, bl, skill_get_splash(sg.skill_id, sg.skill_lv) as i16, sg.bl_flag as u16, Wall::None, |c, t| crate::skillunit::ontimer::skill_trap_splash(c, t, bl.id, now));
        // su->limit = DIFF_TICK(gettick(), sg->tick); sg->unit_id = UNT_USED_TRAPS;
        data::upd_unit(c, bl.id, |u| u.limit = c.tick() - sg.tick);
        data::upd_group(c, sg.group_id, |g| g.unit_id = UNT_USED_TRAPS);
    }
    1
}
