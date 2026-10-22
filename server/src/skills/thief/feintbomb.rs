//! skills/thief/feintbomb.cpp: class SkillFeintBomb : public WeaponSkillImpl (SC_FEINTBOMB).

use crate::battle::calc::Damage;
use crate::conf::area_size;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::area::{foreach_in_range, Wall};
use crate::skill::attack::{skill_blown, BLOWN_IGNORE_NO_KNOCKBACK};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::iface::skill_unitsetting_group_id;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFeintBomb;

impl SkillImpl for SkillFeintBomb {
    fn skill_id(&self) -> u16 {
        SC_FEINTBOMB
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillFeintBomb::calculateSkillRatio: `(skill_lv + 1) * dex / 2 * (sd ? job_level / 10 : 1)` (C integer
    /// arithmetic, left to right), RE_LVL_DMOD(120).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let job = if src.kind == BL_PC { crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32 / 10).unwrap_or(0) } else { 1 };
        *skillratio += -100 + (skill_lv as i32 + 1) * sstatus.dex as i32 / 2 * job;
        re_lvl_dmod(c, src, skillratio, 120);
    }

    /// SkillFeintBomb::castendPos2: the bomb on the caster's cell, every monster attacking the caster switches to the
    /// bomb, the caster slides back, the failed-animation packet and the status.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // Set bomb on current Position
        let group = skill_unitsetting_group_id(c, src, id, skill_lv, x, y, 0);
        // group->unit: the group's first unit (unit[0])
        let first = group.and_then(|g| crate::skillunit::data::units_of(c, g).into_iter().find(|u| u.idx == 0));
        let Some(first) = first else {
            if src.kind == BL_PC {
                skill_fail(c, src, id);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        };
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        // Release all targets against the caster
        foreach_in_range(c, &s, area_size(), BL_MOB, Wall::None, |c, bl| crate::unit::changetarget(c, bl, &s, &first.bl));
        let s = unit::get(c, src.id).unwrap_or(s);
        // Don't stop the caster from backsliding if special_state.no_knockback is active
        skill_blown(c, &s, &s, skill_get_blewcount(id, skill_lv), unit::getdir(&s) as i8, BLOWN_IGNORE_NO_KNOCKBACK);
        let s = unit::get(c, src.id).unwrap_or(s);
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, false);
        sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
