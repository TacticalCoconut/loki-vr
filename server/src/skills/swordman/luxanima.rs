//! skills/swordman/luxanima.cpp: class SkillLuxAnima : public SkillImpl (RK_LUXANIMA).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// status.hpp e_status_change_clear_buffs_flags SCCB_LUXANIMA (not a script constant).
pub const SCCB_LUXANIMA: u8 = 0x10;

pub struct SkillLuxAnima;

impl SkillImpl for SkillLuxAnima {
    fn skill_id(&self) -> u16 {
        RK_LUXANIMA
    }

    /// SkillLuxAnima::castendNoDamageId: the target's SCCB_LUXANIMA buffs end (for bonus_script; status2 agent's
    /// status_change_clear_buffs), then the status.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        iface::status_change_clear_buffs(c, target, SCCB_LUXANIMA); // For bonus_script
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::sc_start(c, Some(src), &t, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let t = unit::get(c, target.id).unwrap_or(t);
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
    }
}
