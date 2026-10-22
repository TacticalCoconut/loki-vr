//! skills/gunslinger/magazineforone.cpp: class SkillMagazineForOne : public WeaponSkillImpl (NW_MAGAZINE_FOR_ONE),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::inventory::{W_GATLING, W_REVOLVER};
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMagazineForOne;

impl SkillImpl for SkillMagazineForOne {
    fn skill_id(&self) -> u16 {
        NW_MAGAZINE_FOR_ONE
    }

    /// SkillMagazineForOne::modifyDamageData: 4 more hits with a gatling gun.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if super::weapontype1(c, src) == Some(W_GATLING) {
            dmg.div_ += 4;
        }
    }

    /// SkillMagazineForOne::castendDamageId: WeaponSkillImpl's hit, then the aim count ends.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
        super::end_intensive_aim_count(c, src);
    }

    /// SkillMagazineForOne::calculateSkillRatio: 250 + 500% per level + 5 x CON, + 100% x level per aim count, + 50 +
    /// 300% per level with a revolver, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 250 + 500 * lv;
        *skillratio += 5 * sstatus.con as i32;
        if let Some(n) = super::intensive_aim_count(c, src) {
            *skillratio += n * 100 * lv;
        }
        if super::weapontype1(c, src) == Some(W_REVOLVER) {
            *skillratio += 50 + 300 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
