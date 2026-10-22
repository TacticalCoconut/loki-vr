//! skills/merchant/carttornado.cpp: class SkillCartTornado : public SkillImplRecursiveDamageSplash (GN_CART_TORNADO).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_BIONIC_WOODENWARRIOR;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillCartTornado::SkillCartTornado() : SkillImplRecursiveDamageSplash(GN_CART_TORNADO).
pub struct SkillCartTornado;

impl SkillImpl for SkillCartTornado {
    fn skill_id(&self) -> u16 {
        GN_CART_TORNADO
    }
    crate::splash_skill_defaults!();

    /// SkillCartTornado::calculateSkillRatio: "ATK [( Skill Level x 200 ) + ( Cart Weight / ( 150 - Caster Base STR ))]
    /// + ( Cart Remodeling Skill Level x 50 )] %" (cart weight in tenths, STR capped at 120), doubled under
    /// SC_BIONIC_WOODENWARRIOR.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 200 * skill_lv as i32;
        if src.kind == BL_PC {
            let cart_weight = crate::inventory::st(c, src.id).map(|s| s.cart_weight).unwrap_or(0) as i32;
            if cart_weight != 0 {
                let str_ = crate::pc::character_of(c, src.id).map(|ch| ch.str as i32).unwrap_or(0);
                *base_skillratio += cart_weight / 10 / (150 - str_.min(120)) + pc_checkskill(c, src, GN_REMODELING_CART) * 50;
            }
        }
        if sce(c, src, SC_BIONIC_WOODENWARRIOR).is_some() {
            *base_skillratio *= 2;
        }
    }

    /// SkillCartTornado::castendNoDamageId: the skill packet, then skill_castend_damage_id around the caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_castend_damage_id(c, &s, &s, id, skill_lv, tick, *flag);
    }

    /// modifyHitRate: +4 per level of GN_REMODELING_CART (players).
    fn modify_hit_rate(&self, c: &Ctx, hit_rate: &mut i16, src: &Unit, _target: &Unit, _skill_lv: u16) {
        let lv = pc_checkskill(c, src, GN_REMODELING_CART);
        if src.kind == BL_PC && lv != 0 {
            *hit_rate = (*hit_rate as i32 + lv * 4) as i16;
        }
    }
}

impl RecursiveDamageSplash for SkillCartTornado {}
