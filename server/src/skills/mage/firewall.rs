//! skills/mage/firewall.cpp: class SkillFireWall : public SkillImpl (MG_FIREWALL). The wall's hits are
//! skill_unit_onplace_timer's UNT_FIREWALL (skillunit/ontimer.rs).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::defs::ELE_FIRE;
use crate::status::sc::battle_check_undead;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFireWall;

impl SkillImpl for SkillFireWall {
    fn skill_id(&self) -> u16 {
        MG_FIREWALL
    }

    /// SkillFireWall::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        //Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        *flag |= 1;
        iface::skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillFireWall::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio -= 50;
    }

    /// SkillFireWall::modifyDamageData.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, _src: &Unit, target: &Unit, _skill_lv: u16) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if tstatus.def_ele as i32 == ELE_FIRE || battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
            dmg.blewcount = 0; // No knockback
        }
    }
}
