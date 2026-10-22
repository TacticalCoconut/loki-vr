//! skills/homunculus/homunculus_castling.cpp: class SkillCastling : public SkillImpl (HAMI_CASTLE).

use super::{fresh, hd_of};
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillCastling;

impl SkillImpl for SkillCastling {
    fn skill_id(&self) -> u16 {
        HAMI_CASTLE
    }

    /// SkillCastling::castendNoDamageId: on another unit (the master: unit_skilluse_id2's auto-target), a 20% per
    /// level chance to swap places with it, a monster targeting it turning to the homunculus; otherwise the master
    /// (or a player target) gets the skill failure.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.id != target.id && c.rnd_chance(20 * skill_lv as i32, 100) {
            // Get one of the monsters targeting the player and set the homunculus as its new target
            if let Some(tbl) = crate::battle::target::battle_gettargeted(c, target).filter(|t| t.kind == BL_MOB) {
                // unit_bl2ud(tbl) != nullptr: every monster has a unit_data
                let s = fresh(c, src);
                crate::skill::unit::unit_changetarget_sub(c, tbl.id, &s);
            }
            let (x, y) = (src.x, src.y);
            // Move homunculus
            if crate::unit::movepos(c, src.id, target.x, target.y, 0, false) {
                crate::battle::clif::clif_blown(c, &fresh(c, src));
                // Move player
                if crate::unit::movepos(c, target.id, x, y, 0, false) {
                    crate::battle::clif::clif_blown(c, &fresh(c, target));
                }
                // Show the animation on the homunculus only
                let s = fresh(c, src);
                clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
            }
        } else if let Some(mu) = hd_of(c, src).filter(|hd| crate::pc::get(c, hd.master).is_some()).and_then(|hd| crate::unit::get(c, hd.master)) {
            // hd->master != nullptr
            skill_fail(c, &mu, id);
        } else if target.kind == BL_PC {
            skill_fail(c, target, id);
        }
    }
}
