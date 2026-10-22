//! skills/gunslinger/onlyonebullet.cpp: class SkillOnlyOneBullet : public WeaponSkillImpl (NW_ONLY_ONE_BULLET), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::inventory::W_REVOLVER;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillOnlyOneBullet;

impl SkillImpl for SkillOnlyOneBullet {
    fn skill_id(&self) -> u16 {
        NW_ONLY_ONE_BULLET
    }

    /// SkillOnlyOneBullet::castendDamageId: WeaponSkillImpl's hit, then the aim count ends.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
        super::end_intensive_aim_count(c, src);
    }

    /// SkillOnlyOneBullet::calculateSkillRatio: 1200 + 3000% per level + 5 x CON, + 350% x level per aim count,
    /// + 400% per level with a revolver, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 1200 + 3000 * lv;
        *skillratio += 5 * sstatus.con as i32;
        if let Some(n) = super::intensive_aim_count(c, src) {
            *skillratio += n * 350 * lv;
        }
        if super::weapontype1(c, src) == Some(W_REVOLVER) {
            *skillratio += 400 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
