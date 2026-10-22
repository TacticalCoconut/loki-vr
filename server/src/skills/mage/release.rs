//! skills/mage/release.cpp: class SkillRelease : public SkillImpl (WL_RELEASE), rAthena e985006. Level 1 casts the
//! last spell sealed by Reading Spellbook (SC_SPELLBOOK1..SC_MAXSPELLBOOK, preserve points in SC_FREEZE_SP's val2);
//! level 2 fires the summoned spheres (SC_SPHERE_1..5) as WL_SUMMON_ATK_* timerskills (skill_timerskill's default
//! case: skill_attack of `skl->type` = BF_MAGIC).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::{skill_castend_damage_id, skill_castend_nodamage_id, skill_castend_pos2};
use crate::skill::castfix::{pc_get_skillcooldown, skill_delayfix};
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::condition::{skill_check_condition_castbegin, skill_consume_requirement};
use crate::skill::cooldown::skill_blockpc_start;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skill::SD_LEVEL;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::defs::BF_MAGIC;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::summonfireball::WLS_FIRE;

pub struct SkillRelease;

impl SkillImpl for SkillRelease {
    fn skill_id(&self) -> u16 {
        WL_RELEASE
    }

    /// SkillRelease::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // status_change *sc = status_get_sc(src); if (sc == nullptr) return;
        if !crate::battle::mobif::has_sc(src.kind) {
            return;
        }
        // map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind != BL_PC {
            return;
        }
        // (#ifndef RENEWAL skill_toggle_magicpower: pre-renewal only, dropped)
        if skill_lv == 1 {
            // SpellBook
            if !sc::has_sce(c, src.id, SC_FREEZE_SP) {
                return;
            }
            // List all available spell to be released
            let Some(i) = (SC_SPELLBOOK1..=SC_MAXSPELLBOOK).rev().find(|&i| sc::has_sce(c, src.id, i)) else { return };
            let Some(book) = sc::get_sce(c, src.id, i) else { return };
            // Now extract the data from the preserved spell
            let pres_skill_id = book.val1 as u16;
            let pres_skill_lv = book.val2 as u16;
            let point = book.val3 as u16;
            sc::status_change_end(c, src.id, i, 0);
            // (rAthena dereferences SC_FREEZE_SP unchecked: it was there above and ending a spellbook does not end it;
            // should it be gone, ending it again is the no-op the else branch does)
            match sc::get_sce(c, src.id, SC_FREEZE_SP) {
                Some(mut freeze) if freeze.val2 > point as i32 => {
                    freeze.val2 -= point as i32;
                    sc::put_sce(c, &freeze);
                }
                _ => {
                    // Last spell to be released
                    sc::status_change_end(c, src.id, SC_FREEZE_SP, 0);
                }
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            if !skill_check_condition_castbegin(c, &s, pres_skill_id, pres_skill_lv) {
                return;
            }
            // Get the requirement for the preserved skill
            skill_consume_requirement(c, &s, pres_skill_id, pres_skill_lv, 1);
            let s = unit::get(c, src.id).unwrap_or(s);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            match skill_get_casttype(pres_skill_id) {
                CastType::Ground => {
                    skill_castend_pos2(c, &s, t.x, t.y, pres_skill_id, pres_skill_lv, tick, 0);
                }
                CastType::NoDamage => {
                    skill_castend_nodamage_id(c, &s, &t, pres_skill_id, pres_skill_lv, tick, 0);
                }
                CastType::Damage => {
                    skill_castend_damage_id(c, &s, &t, pres_skill_id, pres_skill_lv, tick, 0);
                }
            }
            if let Some(mut s) = unit::get(c, src.id) {
                let d = skill_delayfix(c, &s, pres_skill_id, pres_skill_lv) as Tick;
                s.ud.canact_tick = (tick + d).max(s.ud.canact_tick);
                unit::save(c, &s);
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let d = skill_delayfix(c, &s, pres_skill_id, pres_skill_lv);
            crate::status::clif::status_change(c, &s, EFST_POSTDELAY, 1, d as i64, 0, 0, 0);
            let cooldown = pc_get_skillcooldown(c, src.id, pres_skill_id, pres_skill_lv);
            if cooldown > 0 {
                skill_blockpc_start(c, src.id, pres_skill_id, cooldown as Tick);
            }
        } else {
            // Summoned Balls
            for i in (SC_SPHERE_1..=SC_SPHERE_5).rev() {
                let Some(ball) = sc::get_sce(c, src.id, i) else { continue };
                // Convert Ball Element into Skill ATK for balls
                let skele = WL_RELEASE as i32 - 5 + ball.val1 - WLS_FIRE;
                // WL_SUMMON_ATK_FIRE, WL_SUMMON_ATK_WIND, WL_SUMMON_ATK_WATER, WL_SUMMON_ATK_GROUND
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let adelay = crate::status::status_get_status_data(c, &s).adelay as Tick;
                skill_addtimerskill(c, &s, tick + adelay * (i - SC_SPHERE_1).abs() as Tick, target.id, 0, 0, skele as u16, ball.val2 as u16, BF_MAGIC, *flag | SD_LEVEL);
                sc::status_change_end(c, src.id, i, 0); // Eliminate ball
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_nodamage(c, Some(&s), target, id, 0, true);
        }
    }
}
