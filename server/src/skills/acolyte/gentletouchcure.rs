//! skills/acolyte/gentletouchcure.cpp: class SkillGentleTouchCure : public SkillImpl (SR_GENTLETOUCH_CURE).

use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_BLIND, SC_FREEZE, SC_HALLUCINATION, SC_POISON, SC_SILENCE, SC_STONE, SC_STUN};
use crate::status::defs::CLASS_BATTLEFIELD;
use crate::status::{sc, status_get_lv};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillGentleTouchCure;

impl SkillImpl for SkillGentleTouchCure {
    fn skill_id(&self) -> u16 {
        SR_GENTLETOUCH_CURE
    }

    /// SkillGentleTouchCure::castendNoDamageId: heals 120 x level + level% of max HP (not the Emperium or battlefield
    /// monsters), then a chance to cure Stone, Freeze, Stun, Poison, Silence, Blind and Hallucination.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let dstmd = target.kind == BL_MOB;
        if dstmd && (crate::battle::mobif::mob_id(c, target) == crate::battle::mobif::MOBID_EMPERIUM || crate::battle::bl::class_type(c, target) == CLASS_BATTLEFIELD) {
            // heal = 0
        } else {
            // (uint32 arithmetic)
            let max_hp = crate::status::status_get_status_data(c, target).max_hp;
            let heal: u32 = (120 * skill_lv as u32).wrapping_add(max_hp.wrapping_mul(skill_lv as u32) / 100);
            crate::status::hp::status_heal(c, target.id, heal as i64, 0, 0, 0);
        }
        // tsc != nullptr && !tsc->empty() && rnd_chance((level x 5 + (DEX + level) / 4) - rnd_value(1, 10), 100): the
        // argument (with its rnd_value) is evaluated before rnd_chance's own roll
        if crate::battle::mobif::has_sc(target.kind) && !sc::load(c, target.id).empty() {
            let sdex = crate::status::status_get_status_data(c, src).dex as i32;
            let chance = (skill_lv as i32 * 5 + (sdex + status_get_lv(c, src)) / 4) - c.rnd_value(1, 10);
            if c.rnd_chance(chance, 100) {
                for t in [SC_STONE, SC_FREEZE, SC_STUN, SC_POISON, SC_SILENCE, SC_BLIND, SC_HALLUCINATION] {
                    sc::status_change_end(c, target.id, t, 0);
                }
            }
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
    }
}
