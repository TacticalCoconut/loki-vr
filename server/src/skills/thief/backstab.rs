//! skills/thief/backstab.cpp: class SkillBackStab : public SkillImpl (RG_BACKSTAP).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_WEAPON};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::skill_fail;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_HIDING, SC_STUN};
use crate::status::defs::{W_BOW, W_DAGGER};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// conf/battle/skill.conf `backstab_bow_penalty: yes` (no Loki override).
pub fn backstab_bow_penalty() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::BACKSTAB_BOW_PENALTY)
}

pub struct SkillBackStab;

/// sd->status.weapon of a player (None: not a player).
pub(crate) fn weapon_of(c: &Ctx, bl: &Unit) -> Option<i32> {
    if bl.kind != BL_PC {
        return None;
    }
    crate::pc::character_of(c, bl.id).map(|ch| ch.weapon as i32)
}

impl SkillImpl for SkillBackStab {
    fn skill_id(&self) -> u16 {
        RG_BACKSTAP
    }

    /// SkillBackStab::modifyDamageData (RENEWAL): two hits with a dagger.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if weapon_of(c, src) == Some(W_DAGGER) {
            dmg.div_ = 2;
        }
    }

    /// SkillBackStab::calculateSkillRatio: halved with a bow when backstab_bow_penalty is on.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let lv = skill_lv as i32;
        if weapon_of(c, src) == Some(W_BOW) && backstab_bow_penalty() != 0 {
            *base_skillratio += (200 + 40 * lv) / 2;
        } else {
            *base_skillratio += 200 + 40 * lv;
        }
    }

    /// SkillBackStab::castendDamageId (RENEWAL): the caster moves behind the target (one cell past it, seen from the
    /// caster), leaves Hiding, turns the target around and hits; a failed move is a skill failure.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut dir = crate::map::calc_dir(src, target.x, target.y);
        let x: i16 = if dir > 0 && dir < 4 {
            -1
        } else if dir > 4 {
            1
        } else {
            0
        };
        let y: i16 = if dir > 2 && dir < 6 {
            -1
        } else if dir == 7 || dir < 2 {
            1
        } else {
            0
        };
        if crate::battle::target::battle_check_target(c, src, target, BCT_ENEMY) > 0 && unit::movepos(c, src.id, target.x + x, target.y + y, 2, true) {
            // Display movement + animation.
            sc::status_change_end(c, src.id, SC_HIDING, 0);
            dir = if dir < 4 { dir + 4 } else { dir - 4 }; // change direction [Celest]
            if let Some(mut t) = unit::get(c, target.id) {
                unit::setdir(c, &mut t, dir, true);
                unit::save(c, &t);
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            crate::battle::clif::clif_blown(c, &s);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            crate::skill::attack::skill_attack(c, BF_WEAPON, &s, &s, &t, id, skill_lv, tick, *flag);
        } else if src.kind == BL_PC {
            skill_fail(c, src, id);
        }
    }

    /// SkillBackStab::applyAdditionalEffects (RENEWAL): stun.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_STUN, 5 + 2 * skill_lv as i32, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillBackStab::modifyHitRate (RENEWAL): `hit_rate += skill_lv; // !TODO: What's the rate increase?`
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, skill_lv: u16) {
        *hit_rate = hit_rate.wrapping_add(skill_lv as i16);
    }
}
