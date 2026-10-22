//! skills/acolyte/thirdflamebomb.cpp: class SkillThirdFlameBomb : public SkillImplRecursiveDamageSplash
//! (IQ_THIRD_FLAME_BOMB).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::area;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_SECOND_BRAND;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillThirdFlameBomb;

impl SkillImpl for SkillThirdFlameBomb {
    fn skill_id(&self) -> u16 {
        IQ_THIRD_FLAME_BOMB
    }

    crate::splash_skill_defaults!();

    /// SkillThirdFlameBomb::modifyDamageData: one more hit per 5 spheres beyond the first 5 (the splash's
    /// skill_area_temp[0], passed as miscflag); number of hits doesn't go above 3.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        dmg.div_ = ((dmg.div_ as i32 + dmg.miscflag).min(3)) as i16; // Number of hits doesn't go above 3.
    }

    /// SkillThirdFlameBomb::calculateSkillRatio: 650% per level + 10 x POW + 20% of the caster's max HP
    /// (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 650 * skill_lv as i32 + 10 * sstatus.pow as i32;
        // (uint32 arithmetic added to the int32 ratio)
        *skillratio = (*skillratio as u32).wrapping_add(sstatus.max_hp.wrapping_mul(20) / 100) as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillThirdFlameBomb::applyAdditionalEffects: the target's SC_SECOND_BRAND ends.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::status_change_end(c, target.id, SC_SECOND_BRAND, 0);
    }
}

impl RecursiveDamageSplash for SkillThirdFlameBomb {
    /// SkillThirdFlameBomb::splashSearch: the animation on the target; a player with 10 or more spheres gets
    /// skill_area_temp[0] = spheres / 5 - 1 (the extra hits); then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        if src.kind == BL_PC {
            let spiritball = crate::skill::state::get(c, src.id).spiritball as i32;
            if spiritball / 5 > 1 {
                area::set_temp(0, spiritball / 5 - 1);
            }
        }
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
