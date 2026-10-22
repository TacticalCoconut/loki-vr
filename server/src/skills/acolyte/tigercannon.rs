//! skills/acolyte/tigercannon.cpp: class SkillTigerCannon : public WeaponSkillImpl (SR_TIGERCANNON).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::{SD_ANIMATION, SD_SPLASH};
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_COMBO, SC_FLASHCOMBO, SC_GT_REVITALIZE};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillTigerCannon;

impl SkillImpl for SkillTigerCannon {
    fn skill_id(&self) -> u16 {
        SR_TIGERCANNON
    }

    /// SkillTigerCannon::calculateSkillRatio: from the HP / SP the cast consumed (uint32 arithmetic): after Fallen
    /// Empire (miscflag & 8) [((HP + SP) / 2) x base level / 100]%, else [((HP + SP) / 4) x base level / 100]%;
    /// +30% under Gentle Touch - Revitalize.
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as u32;
        let hp: u32 = sstatus.max_hp.wrapping_mul(10 + lv * 2) / 100;
        let sp: u32 = sstatus.max_sp.wrapping_mul(5 + lv) / 100;
        let add = if wd.miscflag & 8 != 0 {
            // Base_Damage = [((Caster consumed HP + SP) / 2) x Caster Base Level / 100] %
            (-100i32 as u32).wrapping_add(hp.wrapping_add(sp) / 2)
        } else {
            // Base_Damage = [((Caster consumed HP + SP) / 4) x Caster Base Level / 100] %
            (-100i32 as u32).wrapping_add(hp.wrapping_add(sp) / 4)
        };
        *skillratio = (*skillratio as u32).wrapping_add(add) as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_GT_REVITALIZE).is_some() {
            *skillratio += *skillratio * 30 / 100;
        }
    }

    /// SkillTigerCannon::castendDamageId: flag & 1: the hit (SD_ANIMATION); else a player's splash around the
    /// target, with the combo bonus (flag |= 8 on the caller's flag) after Fallen Empire unless cast through Flash
    /// Combo.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            let sflag = *flag | SD_ANIMATION;
            weapon_castend_damage_id(c, id, src, target, skill_lv, tick, sflag);
        } else if src.kind == BL_PC {
            if sce(c, src, SC_COMBO).is_some_and(|e| e.val1 == SR_FALLENEMPIRE as i32) && sce(c, src, SC_FLASHCOMBO).is_none() {
                *flag |= 8; // Only apply Combo bonus when Tiger Cannon is not used through Flash Combo
            }
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        }
    }

    /// SkillTigerCannon::castendNoDamageId: the animation, then skill_castend_damage_id on the caster (the splash
    /// around it).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_castend_damage_id(c, &s, &s, id, skill_lv, tick, *flag);
    }
}
