//! skills/archer/severerainstorm.cpp: class SkillSevereRainstorm : public SkillImpl (WM_SEVERE_RAINSTORM) and class
//! SkillSevereRainstormMelee : public WeaponSkillImpl (WM_SEVERE_RAINSTORM_MELEE: the hits of its unit).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSevereRainstorm;

impl SkillImpl for SkillSevereRainstorm {
    fn skill_id(&self) -> u16 {
        WM_SEVERE_RAINSTORM
    }

    /// SkillSevereRainstorm::castendPos2: `flag |= 1`; a player cannot switch equipment for the skill's duration
    /// (`sd->canequip_tick = tick + skill_get_time(…)`: skillcore's PcSkill copy of the field); skill_unitsetting.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        *flag |= 1;
        if src.kind == crate::map::BL_PC {
            // Can't switch equips for the duration of the skill.
            let t = skill_get_time(id, skill_lv) as i64;
            crate::skill::state::with(c, src.id, |s| s.canequip_tick = tick + t);
        }
        crate::skill::iface::skill_unitsetting(c, src, id, skill_lv, x, y, 0);
    }
}

pub struct SkillSevereRainstormMelee;

impl SkillImpl for SkillSevereRainstormMelee {
    fn skill_id(&self) -> u16 {
        WM_SEVERE_RAINSTORM_MELEE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSevereRainstormMelee::calculateSkillRatio: ATK [{(Caster DEX / 300 + AGI / 200)} x Caster Base Level /
    /// 100] %: -100 + 100 * lv + (DEX / 300 + AGI / 200), + 20 * lv with a whip / instrument (wd->miscflag & 4),
    /// RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 100 * skill_lv as i32 + (sstatus.dex as i32 / 300 + sstatus.agi as i32 / 200);
        if wd.miscflag & 4 != 0 {
            // Whip/Instrument equipped
            *skillratio += 20 * skill_lv as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
