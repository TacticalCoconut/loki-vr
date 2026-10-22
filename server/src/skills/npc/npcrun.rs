//! skills/npc/npcrun.cpp: class SkillNpcRun : public SkillImpl (NPC_RUN), and unit.cpp unit_escape, the escape walk
//! it and randommove.cpp (NPC_RANDOMMOVE) use (their only users in rAthena).

use crate::db::skills::*;
use crate::map::{CellChk, BL_MOB};
use crate::mob::MSS_WALK;
use crate::path::{DIRX, DIRY};
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillNpcRun;

impl SkillImpl for SkillNpcRun {
    fn skill_id(&self) -> u16 {
        NPC_RUN
    }

    /// SkillNpcRun::castendNoDamageId: a monster runs away from its target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        // mob_data* md = BL_CAST(BL_MOB, src)
        if src.kind != BL_MOB {
            return;
        }
        let Some(mut md) = crate::mob::get(c, src.id) else { return };
        // block_list* tbl = map_id2bl(md->target_id)
        let Some(tbl) = (md.target_id != 0).then(|| unit::get(c, md.target_id)).flatten() else { return };
        md.can_escape = true;
        crate::mob::put(c, &md);
        crate::mob::ai::mob_unlocktarget(c, md.id, tick);
        // Official distance is 7, if level > 1, distance = level
        let dist = if skill_lv > 1 { skill_lv as i16 } else { 7 };
        let time = unit_escape(c, src.id, &tbl, dist, 3);
        if time != 0 {
            if let Some(mut md) = crate::mob::get(c, src.id) {
                // Need to set state here as it's not set otherwise
                crate::mob::mob_setstate(&mut md, MSS_WALK);
                // Set AI to inactive for the duration of this movement
                md.next_thinktime = tick + time;
                crate::mob::put(c, &md);
            }
        }
    }
}

/// unit.cpp unit_escape: unit `bl_id` runs away from `target` up to `dist` cells, straight away from it (the
/// direction from the target to the unit). flag &1: straight line escape, the path stops before the first
/// unreachable cell; else the furthest reachable cell on that line (then a walkpath to it). `flag` is unit_walktoxy's.
/// Returns how long the walk takes (unit_get_walkpath_time), 0 when the unit does not walk.
pub(crate) fn unit_escape(c: &Ctx, bl_id: u32, target: &Unit, dist: i16, flag: u8) -> Tick {
    let Some(bl) = unit::get(c, bl_id) else { return 0 };
    let dir = crate::map::calc_dir(target, bl.x, bl.y) as usize;
    let (dx, dy) = (DIRX[dir] as i32, DIRY[dir] as i32);
    let (x, y) = (bl.x as i32, bl.y as i32);
    let mut dist = dist;
    if flag & 1 != 0 {
        // Straight line escape
        // Keep moving until we hit an unreachable cell
        let mut i: i16 = 1;
        while i <= dist {
            if crate::map::getcell(c, bl.map, (x + i as i32 * dx) as i16, (y + i as i32 * dy) as i16, CellChk::ChkNoReach) {
                dist = i - 1;
            }
            i += 1;
        }
    } else {
        // Find the furthest reachable cell (then find a walkpath to it)
        while dist > 0 && crate::map::getcell(c, bl.map, (x + dist as i32 * dx) as i16, (y + dist as i32 * dy) as i16, CellChk::ChkNoReach) {
            dist -= 1;
        }
    }
    if dist > 0 && unit::walktoxy(c, bl.id, (x + dist as i32 * dx) as i16, (y + dist as i32 * dy) as i16, flag as i32) {
        let u = unit::get(c, bl.id).unwrap_or(bl);
        return crate::mob::unit::unit_get_walkpath_time(&u);
    }
    0
}
