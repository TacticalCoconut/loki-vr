//! skills/npc/comet2.cpp: class SkillComet2 : public SkillImpl (NPC_COMET), rAthena e985006. Its hits come from its
//! skill unit (skill_unitsetting: skillunit agent); the damage depends on the distance to where the caster's comet
//! fell (status_change::comet_x / comet_y, set by the unit).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::consts::SC_BURNING;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillComet2;

impl SkillImpl for SkillComet2 {
    fn skill_id(&self) -> u16 {
        NPC_COMET
    }

    /// SkillComet2::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start4(c, Some(src), target, SC_BURNING, 100, skill_lv as i32, 1000, src.id as i32, 0, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillComet2::calculateSkillRatio: 2500% + 500% per level, less 500% per two cells from the comet's center
    /// (at least one, at most four steps; 8 cells when the caster has no status_change).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        // const status_change *sc = status_get_sc(src)
        let d = if crate::battle::mobif::has_sc(src.kind) {
            let us = sc::unit_sc(c, src.id);
            // distance_xy(target->x, target->y, sc->comet_x, sc->comet_y)
            crate::path::distance(target.x as i32 - us.comet_x as i32, target.y as i32 - us.comet_y as i32)
        } else {
            8
        };
        let i = (d / 2).clamp(1, 4); // cap_value(i, 1, 4)
        *base_skillratio = 2500 + ((skill_lv as i32 - i + 1) * 500);
    }

    /// SkillComet2::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
