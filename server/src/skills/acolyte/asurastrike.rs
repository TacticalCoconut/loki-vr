//! skills/acolyte/asurastrike.cpp: class SkillAsuraStrike : public WeaponSkillImpl (MO_EXTREMITYFIST).

use crate::battle::calc::Damage;
use crate::clif::Target;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_BLADESTOP, SC_EXPLOSIONSPIRITS, SC_EXTREMITYFIST};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAsuraStrike;

impl SkillImpl for SkillAsuraStrike {
    fn skill_id(&self) -> u16 {
        MO_EXTREMITYFIST
    }

    /// SkillAsuraStrike::castendDamageId: the hit (+100% with more than 5 spheres used, RENEWAL: `flag |= 1` on the
    /// caller's flag, which also makes skill_castend_damage_id skip the requirement consumption, as in rAthena), all
    /// SP gone, SC_EXTREMITYFIST, Fury and Blade Stop end, then the caster moves 3 cells toward the target's side.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let i: i16 = 3; // Move 3 cells (From caster)
        let dir = crate::map::calc_dir(src, target.x, target.y) as i32;
        if src.kind == BL_PC && crate::skill::state::get(c, src.id).spiritball_old > 5 {
            *flag |= 1; // Give +100% damage increase
        }
        weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);

        crate::status::hp::status_set_sp(c, src.id, 0, 0);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, SC_EXTREMITYFIST, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        sc::status_change_end(c, src.id, SC_EXPLOSIONSPIRITS, 0);
        sc::status_change_end(c, src.id, SC_BLADESTOP, 0);

        let x: i16 = if dir > 0 && dir < 4 {
            -i
        } else if dir > 4 {
            i
        } else {
            0
        };
        let y: i16 = if dir > 2 && dir < 6 {
            -i
        } else if dir == 7 || dir < 2 {
            i
        } else {
            0
        };
        let s = unit::get(c, src.id).unwrap_or(s);
        if unit::movepos(c, src.id, s.x + x, s.y + y, 1, true) {
            if let Some(s) = unit::get(c, src.id) {
                crate::battle::clif::clif_blown(c, &s);
                crate::skill::clif::clif_spiritball(c, &s, None, Target::Area);
            }
        }
    }

    /// SkillAsuraStrike::calculateSkillRatio: +700% + 10% per SP; doubled with more than 5 spirit balls (RENEWAL,
    /// wd->miscflag & 1); capped at 500000 for overflow protection (roughly 50k SP).
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        // (sstatus->sp is uint32: the sum is uint32 arithmetic folded into the int32 ratio)
        *base_skillratio = (*base_skillratio as u32).wrapping_add(700u32.wrapping_add(sstatus.sp.wrapping_mul(10))) as i32;
        if wd.miscflag & 1 != 0 {
            *base_skillratio = base_skillratio.wrapping_mul(2); // More than 5 spirit balls active
        }
        *base_skillratio = (*base_skillratio).min(500000); // We stop at roughly 50k SP for overflow protection
    }
}
