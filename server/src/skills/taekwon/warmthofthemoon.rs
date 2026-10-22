//! skills/taekwon/warmthofthemoon.cpp: class SkillWarmthoftheMoon : public SkillImpl (SG_MOON_WARM).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWarmthoftheMoon;

impl SkillImpl for SkillWarmthoftheMoon {
    fn skill_id(&self) -> u16 {
        SG_MOON_WARM
    }

    /// SkillWarmthoftheMoon::modifyDamageData.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        // A random 0~3 knockback bonus is added to the base knockback
        dmg.blewcount += c.rnd_value(0, 3);
    }

    /// SkillWarmthoftheMoon::castendPos2: the unit follows the caster (placed on its cell whatever the target cell), SC_WARM
    /// remembers the group (val4).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        crate::skillunit::skill_clear_unitgroup(c, src.id);
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        if let Some(group_id) = crate::skill::iface::skill_unitsetting_group_id(c, &s, id, skill_lv, s.x, s.y, 0) {
            let s = crate::unit::get(c, src.id).unwrap_or_else(|| s.clone());
            sc::sc_start4(c, Some(&s), &s, type_, 100, skill_lv as i32, 0, 0, group_id, skill_get_time(id, skill_lv) as i64);
        }
        *flag |= 1;
    }
}
