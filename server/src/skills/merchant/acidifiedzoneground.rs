//! skills/merchant/acidifiedzoneground.cpp: class SkillAcidifiedZoneGround : public SkillImplRecursiveDamageSplash (BO_ACIDIFIED_ZONE_GROUND); class
//! SkillActifiedZoneGroundAttack : public WeaponSkillImpl (BO_ACIDIFIED_ZONE_GROUND_ATK, the hits of the zone's unit).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};
use super::acidifiedzonefire::acidified_zone_ratio;

/// SkillAcidifiedZoneGround::SkillAcidifiedZoneGround() : SkillImplRecursiveDamageSplash(BO_ACIDIFIED_ZONE_GROUND).
pub struct SkillAcidifiedZoneGround;

impl SkillImpl for SkillAcidifiedZoneGround {
    fn skill_id(&self) -> u16 {
        BO_ACIDIFIED_ZONE_GROUND
    }

    /// SkillImplRecursiveDamageSplash::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// calculateSkillRatio (every BO_ACIDIFIED_ZONE_* is the same: "All BO_ACIDIFIED_ZONE_* deal the same damage?
    /// [Rytech]"): 400% per level + POW x 5; under SC_RESEARCHREPORT x1.5, and x1.5 again on formless / plant
    /// targets; RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        acidified_zone_ratio(c, src, target, skill_lv, skillratio);
    }

    /// castendPos2: the zone unit; flag |= 1 so the ammo is not consumed now (deleted with the group).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        crate::skill::iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}

impl RecursiveDamageSplash for SkillAcidifiedZoneGround {
    /// splashSearch: the skill packet on the target; on a player a single-cell zone on its cell (skill_castend_pos2,
    /// flag 0); then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        if target.kind == BL_PC {
            // Place single cell AoE if hitting a player.
            crate::skill::castend::skill_castend_pos2(c, src, target.x, target.y, id, skill_lv, tick, 0);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        crate::skills::splash_search_default(self, c, &s, &t, skill_lv, tick, flag);
    }
}

/// SkillActifiedZoneGroundAttack::SkillActifiedZoneGroundAttack() : WeaponSkillImpl(BO_ACIDIFIED_ZONE_GROUND_ATK).
pub struct SkillActifiedZoneGroundAttack;

impl SkillImpl for SkillActifiedZoneGroundAttack {
    fn skill_id(&self) -> u16 {
        BO_ACIDIFIED_ZONE_GROUND_ATK
    }

    /// WeaponSkillImpl::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// calculateSkillRatio (every BO_ACIDIFIED_ZONE_* is the same: "All BO_ACIDIFIED_ZONE_* deal the same damage?
    /// [Rytech]"): 400% per level + POW x 5; under SC_RESEARCHREPORT x1.5, and x1.5 again on formless / plant
    /// targets; RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        acidified_zone_ratio(c, src, target, skill_lv, skillratio);
    }
}
