//! skills/gunslinger/grounddrift.cpp: class SkillGroundDrift : public SkillImpl (GS_GROUNDDRIFT), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::iface::skill_unitsetting;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGroundDrift;

impl SkillImpl for SkillGroundDrift {
    fn skill_id(&self) -> u16 {
        GS_GROUNDDRIFT
    }

    /// SkillGroundDrift::modifyDamageData: the source's attack motion, no knock-back.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        dmg.amotion = crate::status::status_get_status_data(c, src).amotion as i32;
        dmg.blewcount = 0;
    }

    /// SkillGroundDrift::calculateSkillRatio (RENEWAL: + 100 + 20% per level).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 100 + 20 * skill_lv as i32;
    }

    /// SkillGroundDrift::castendPos2: the grenade unit (ammo should be deleted right away: no `flag |= 1`).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }

    /// SkillGroundDrift::modifyElement: element comes in flag (the unit's attack passes it as the damage's miscflag).
    fn modify_element(&self, _c: &Ctx, dmg: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        *element = dmg.miscflag;
    }
}
