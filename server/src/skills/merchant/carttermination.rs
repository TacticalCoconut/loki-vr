//! skills/merchant/carttermination.cpp: class SkillCartTermination : public WeaponSkillImpl (WS_CARTTERMINATION).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::SkillImpl;
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillCartTermination::SkillCartTermination() : WeaponSkillImpl(WS_CARTTERMINATION).
pub struct SkillCartTermination;

impl SkillImpl for SkillCartTermination {
    fn skill_id(&self) -> u16 {
        WS_CARTTERMINATION
    }

    /// WeaponSkillImpl::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillCartTermination::calculateSkillRatio: the cart's weight divided by 10 * (16 - lv), scaled to
    /// battle_config.max_cart_weight (conf/battle/player.conf `max_cart_weight: 8000`, x 10: conf.rs MAX_CART_WEIGHT)
    /// to preserve the damage ratio when the maximum cart weight is changed; non-players deal the maximum.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let mut i = 10 * (16 - skill_lv as i32);
        if i < 1 {
            i = 1;
        }
        // Preserve damage ratio when max cart weight is changed.
        if src.kind == BL_PC {
            let cart_weight = crate::inventory::st(c, src.id).map(|s| s.cart_weight as i32).unwrap_or(0);
            if cart_weight != 0 {
                *base_skillratio += cart_weight / i * 80000 / crate::conf::max_cart_weight() - 100;
            }
        } else {
            *base_skillratio += 80000 / i - 100;
        }
    }

    /// SkillCartTermination::applyAdditionalEffects: stun, 5 * lv %.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 5 * skill_lv as i32, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
