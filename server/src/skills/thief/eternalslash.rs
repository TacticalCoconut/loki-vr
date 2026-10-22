//! skills/thief/eternalslash.cpp: class SkillEternalSlash : public WeaponSkillImpl (SHC_ETERNAL_SLASH).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_E_SLASH_COUNT, SC_SHADOW_EXCEED};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillEternalSlash;

impl SkillImpl for SkillEternalSlash {
    fn skill_id(&self) -> u16 {
        SHC_ETERNAL_SLASH
    }

    /// SkillEternalSlash::modifyDamageData: as many hits as the slash counter.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if let Some(e) = sce(c, src, SC_E_SLASH_COUNT) {
            dmg.div_ = e.val1 as i16;
        }
    }

    /// SkillEternalSlash::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 300 * lv + 2 * sstatus.pow as i32;
        if sce(c, src, SC_SHADOW_EXCEED).is_some() {
            *skillratio += 120 * lv + sstatus.pow as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillEternalSlash::castendDamageId: the slash counter goes up (max 5), the animation, then the hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let time = skill_get_time(id, skill_lv) as i64;
        if let Some(e) = sce(c, src, SC_E_SLASH_COUNT) {
            sc::sc_start(c, Some(src), src, SC_E_SLASH_COUNT, 100, 5.min(1 + e.val1), time);
        } else {
            sc::sc_start(c, Some(src), src, SC_E_SLASH_COUNT, 100, 1, time);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true);
        weapon_castend_damage_id(c, id, &s, target, skill_lv, tick, *flag);
    }
}
