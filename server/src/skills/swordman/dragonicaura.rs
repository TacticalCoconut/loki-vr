//! skills/swordman/dragonicaura.cpp: class SkillDragonicAura : public WeaponSkillImpl (DK_DRAGONIC_AURA).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::defs::{RC_ANGEL, RC_DEMIHUMAN};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDragonicAura;

impl SkillImpl for SkillDragonicAura {
    fn skill_id(&self) -> u16 {
        DK_DRAGONIC_AURA
    }

    /// SkillDragonicAura::castendDamageId: the hit, then the caster's status.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }

    /// SkillDragonicAura::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);
        let lv = skill_lv as i32;
        *skillratio += 3650 * lv + 10 * sstatus.pow as i32;
        if tstatus.race as i32 == RC_DEMIHUMAN || tstatus.race as i32 == RC_ANGEL {
            *skillratio += 150 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
