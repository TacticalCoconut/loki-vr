//! skills/merchant/axeboomerang.cpp: class SkillAxeBoomerang : public WeaponSkillImpl (NC_AXEBOOMERANG).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillAxeBoomerang::SkillAxeBoomerang() : WeaponSkillImpl(NC_AXEBOOMERANG).
pub struct SkillAxeBoomerang;

impl SkillImpl for SkillAxeBoomerang {
    fn skill_id(&self) -> u16 {
        NC_AXEBOOMERANG
    }

    /// WeaponSkillImpl::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillAxeBoomerang::calculateSkillRatio: +150% + 50% per level, + the right-hand weapon's weight / 10 ("10
    /// weight in coding make 1 whole actual weight"), RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 150 + 50 * skill_lv as i32;
        if src.kind == BL_PC {
            // int16 index = sd->equip_index[EQI_HAND_R]
            let index = crate::inventory::st(c, src.id).and_then(|s| s.equip_index.get(crate::inventory::EQI_HAND_R).copied()).unwrap_or(-1);
            if index >= 0 {
                let data = crate::inventory::inv_get(c, src.id, index as usize).and_then(|r| crate::db::items::itemdb_exists(r.item.nameid));
                if let Some(d) = data.filter(|d| d.r#type == crate::db::items::IT_WEAPON) {
                    *skillratio += d.weight as i32 / 10;
                }
            }
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
