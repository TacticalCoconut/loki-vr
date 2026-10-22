//! skills/merchant/cartrevolution.cpp: class SkillCartRevolution : public SkillImplRecursiveDamageSplash
//! (MC_CARTREVOLUTION). Its knockback direction (battle_config.cart_revo_knockback) is skill_blown's.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::SD_PREAMBLE;
use crate::skills::novice::pc_checkskill;
use crate::skills::{splash_castend_damage_id, splash_castend_pos2, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillCartRevolution::SkillCartRevolution() : SkillImplRecursiveDamageSplash(MC_CARTREVOLUTION).
pub struct SkillCartRevolution;

impl SkillImpl for SkillCartRevolution {
    fn skill_id(&self) -> u16 {
        MC_CARTREVOLUTION
    }

    /// SkillCartRevolution::castendDamageId: a fake packet will be sent for the first target to be hit
    /// (flag |= SD_PREAMBLE, written back to the caller's flag as rAthena's `int32&`), then the splash parent's.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        *flag |= SD_PREAMBLE; // a fake packet will be sent for the first target to be hit
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (inherited).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }

    /// SkillCartRevolution::calculateSkillRatio: +50%, then +1% for every 1% of cart weight (players), +100% for
    /// non-players (max damage).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50;
        // const map_session_data *sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            let (w, wmax) = crate::inventory::st(c, src.id).map(|s| (s.cart_weight as i32, s.cart_weight_max as i32)).unwrap_or((0, 0));
            if w != 0 {
                // +1% every 1% weight (cart_weight_max is never 0 while the cart holds weight; a zero divisor adds
                // nothing here instead of crashing)
                *base_skillratio += (100 * w).checked_div(wmax).unwrap_or(0);
            }
        } else {
            *base_skillratio += 100; // Max damage for non players.
        }
    }

    /// SkillCartRevolution::modifyHitRate: +4% hit per GN_REMODELING_CART level.
    fn modify_hit_rate(&self, c: &Ctx, hit_rate: &mut i16, src: &Unit, _target: &Unit, _skill_lv: u16) {
        let lv = pc_checkskill(c, src, GN_REMODELING_CART);
        if lv != 0 {
            *hit_rate = hit_rate.wrapping_add((lv * 4) as i16);
        }
    }
}

impl RecursiveDamageSplash for SkillCartRevolution {}
