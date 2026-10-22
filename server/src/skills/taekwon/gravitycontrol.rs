//! skills/taekwon/gravitycontrol.cpp: class SkillGravityControl : public SkillImpl (SJ_GRAVITYCONTROL).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGravityControl;

impl SkillImpl for SkillGravityControl {
    fn skill_id(&self) -> u16 {
        SJ_GRAVITYCONTROL
    }

    /// SkillGravityControl::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let sstatus = crate::status::status_get_status_data(c, src);
        let tstatus = crate::status::status_get_status_data(c, target);

        let mut fall_damage: i32 = sstatus.batk.wrapping_add(sstatus.rhw.atk as i32).wrapping_sub(tstatus.def2 as i32);

        if target.kind == BL_PC {
            // dstsd->weight / 10 - tstatus->def: uint32 arithmetic, wrapping back into the int32
            let weight = crate::inventory::st(c, target.id).map(|s| s.weight).unwrap_or(0);
            fall_damage = fall_damage.wrapping_add((weight / 10) as i32).wrapping_sub(tstatus.def as i32);
        } else {
            // Monster's don't have weight. Put something in its place.
            fall_damage = fall_damage.wrapping_add((50 * crate::status::status_get_lv(c, src)).wrapping_sub(tstatus.def as i32));
        }

        fall_damage = std::cmp::max(1, fall_damage);

        let ok = sc::sc_start2(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, fall_damage, skill_get_time(id, skill_lv) as i64);
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
    }
}
