//! skills/npc/npccursedcircle.cpp: class SkillNpcCursedCircle : public SkillImpl (NPC_SR_CURSEDCIRCLE), rAthena
//! e985006; and map.cpp map_forcountinrange, which the module did not have yet (skill/area.rs is its natural home;
//! SR_CURSEDCIRCLE of the monk family uses it too).

use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_MOB, BL_PC};
use crate::skill::area::{self, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::consts::SC_CURSEDCIRCLE_ATKER;
use crate::status::defs::CLASS_BOSS;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillNpcCursedCircle;

impl SkillImpl for SkillNpcCursedCircle {
    fn skill_id(&self) -> u16 {
        NPC_SR_CURSEDCIRCLE
    }

    /// SkillNpcCursedCircle::castendNoDamageId: the enemies around the caster are held (flag & 1: one of them; a
    /// monster also forgets its target); the caster gets SC_CURSEDCIRCLE_ATKER with the number of units counted
    /// (a player spends that many spirit spheres; others count as having 15).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            if crate::battle::bl::class_type(c, target) == CLASS_BOSS {
                return;
            }
            if sc::sc_start2(c, Some(src), target, skill_get_sc(id) as i32, 50, skill_lv as i32, src.id as i32, skill_get_time(id, skill_lv) as i64) {
                if target.kind == BL_MOB {
                    // mob_unlocktarget((TBL_MOB*)target, gettick())
                    crate::mob::ai::mob_unlocktarget(c, target.id, c.tick());
                }
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                crate::status::sc_ext::clif_bladestop(c, &s, target.id, true);
                *flag |= SKILL_NOCONSUME_REQ;
            }
        } else {
            // map_session_data* sd = BL_CAST(BL_PC, src)
            let sd = src.kind == BL_PC;
            clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
            // Assume 15 spiritballs in non-characters
            let max = if sd { crate::skill::state::get(c, src.id).spiritball_old as i32 } else { 15 };
            let f = *flag | BCT_ENEMY | 1;
            let count = map_forcountinrange(c, src, skill_get_splash(id, skill_lv) as i16, max, BL_CHAR, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
            if sd {
                crate::skill::state::pc_delspiritball(c, src.id, count, 0);
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let ok = sc::sc_start2(c, Some(&s), &s, SC_CURSEDCIRCLE_ATKER, 50, skill_lv as i32, count, skill_get_time(id, skill_lv) as i64);
            let s = unit::get(c, src.id).unwrap_or(s);
            clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, ok);
        }
    }
}

/// map.cpp map_forcountinrange: map_foreachinrange's selection with no wall check (the units of `kinds` in the
/// square `range` around `center`, clamped to the map, in rAthena's block order), `f` on each one still on the map,
/// stopping once the sum of what `f` returned reaches `count` (0: no limit). Returns that sum.
pub(crate) fn map_forcountinrange(c: &Ctx, center: &Unit, range: i16, count: i32, kinds: u16, mut f: impl FnMut(&Ctx, &Unit) -> i32) -> i32 {
    let ids = area::collect_in_range(c, center, range, kinds, Wall::None);
    let mut return_count = 0;
    for id in ids {
        let Some(u) = unit::get(c, id) else { continue };
        if !u.on_map {
            continue; // func() may delete this bl_list[] slot, checking for prev ensures it wasn't queued for deletion.
        }
        return_count += f(c, &u);
        if count != 0 && return_count >= count {
            break;
        }
    }
    return_count
}
