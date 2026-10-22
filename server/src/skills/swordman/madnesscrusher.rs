//! skills/swordman/madnesscrusher.cpp: class SkillMadnessCrusher : public SkillImplRecursiveDamageSplash
//! (DK_MADNESS_CRUSHER).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::inventory::EQI_HAND_R;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CHARGINGPIERCE_COUNT;
use crate::unit::Unit;
use crate::world::Ctx;

pub struct SkillMadnessCrusher;

impl SkillImpl for SkillMadnessCrusher {
    fn skill_id(&self) -> u16 {
        DK_MADNESS_CRUSHER
    }

    crate::splash_skill_defaults!();

    /// SkillMadnessCrusher::calculateSkillRatio: the right-hand weapon's weight / 10 * weapon level (uint32) counts.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 1750 + 4350 * skill_lv as i32;
        *skillratio += 10 * sstatus.pow as i32;
        if let Some(sd) = crate::battle::bl::sd(c, src) {
            // index = sd->equip_index[EQI_HAND_R]; index >= 0 && sd->inventory_data[index] != nullptr
            if let Some((_, data)) = crate::battle::bl::equip(c, &sd, EQI_HAND_R) {
                let add = (data.weight / 10).wrapping_mul(data.weapon_level as u32);
                *skillratio = (*skillratio as u32).wrapping_add(add) as i32;
            }
        }
        re_lvl_dmod(c, src, skillratio, 100);
        if sce(c, src, SC_CHARGINGPIERCE_COUNT).map(|e| e.val1 >= 10).unwrap_or(false) {
            *skillratio *= 2;
        }
    }
}

impl RecursiveDamageSplash for SkillMadnessCrusher {}
