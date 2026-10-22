//! skills/swordman/spiralpierce.cpp: class SkillSpiralPierce : public WeaponSkillImpl (LK_SPIRALPIERCE).

use crate::battle::calc::Damage;
use crate::battle::{BF_LONG, BF_MISC, BF_RANGEMASK, BF_WEAPONMASK};
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_ANKLE, SC_CHARGINGPIERCE_COUNT};
use crate::status::defs::{ELE_NEUTRAL, MD_STATUSIMMUNE};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSpiralPierce;

impl SkillImpl for SkillSpiralPierce {
    fn skill_id(&self) -> u16 {
        LK_SPIRALPIERCE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSpiralPierce::modifyDamageData: a non-player's Spiral Pierce is a long-range misc attack.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if src.kind != BL_PC {
            dmg.flag = (dmg.flag & !(BF_RANGEMASK | BF_WEAPONMASK)) | BF_LONG | BF_MISC;
        }
    }

    /// SkillSpiralPierce::calculateSkillRatio (RENEWAL).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 50 + 50 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_CHARGINGPIERCE_COUNT).map(|e| e.val1 >= 10).unwrap_or(false) {
            *skillratio *= 2;
        }
    }

    /// SkillSpiralPierce::applyAdditionalEffects: does not work on status immune.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let dstsd = target.kind == BL_PC;
        let dstmd = target.kind == BL_MOB;
        if dstsd || (dstmd && !crate::battle::bl::has_mode(c, target, MD_STATUSIMMUNE)) {
            sc::sc_start(c, Some(src), target, SC_ANKLE, 100, 0, skill_get_time2(self.skill_id(), skill_lv) as i64);
        }
    }

    /// SkillSpiralPierce::modifyElement: forced neutral for monsters (any non-player).
    fn modify_element(&self, _c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if src.kind != BL_PC {
            *element = ELE_NEUTRAL;
        }
    }
}
