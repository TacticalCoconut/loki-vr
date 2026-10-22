//! skills/swordman/phantomthrust.cpp: class SkillPhantomThrust : public WeaponSkillImpl (RK_PHANTOMTHRUST).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::{skill_blown, BLOWN_NONE};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillPhantomThrust;

impl SkillImpl for SkillPhantomThrust {
    fn skill_id(&self) -> u16 {
        RK_PHANTOMTHRUST
    }

    /// SkillPhantomThrust::calculateSkillRatio: ATK = [{(Skill Level x 50) + (Spear Master Level x 10)} x Caster's
    /// Base Level / 150] % (non-players count Spear Mastery 5).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let spear_mastery = if src.kind == BL_PC { pc_checkskill(c, src, KN_SPEARMASTERY) } else { 5 };
        *skillratio += -100 + 50 * skill_lv as i32 + 10 * spear_mastery;
        re_lvl_dmod(c, src, skillratio, 150); // Base level bonus.
    }

    /// SkillPhantomThrust::castendDamageId: the caster turns to the target and pulls it next to itself; an enemy is
    /// hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let dir = crate::map::calc_dir(&s, target.x, target.y);
        unit::setdir(c, &mut s, dir, true);
        unit::save(c, &s);
        clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true);
        let dist = crate::path::distance((s.x - target.x) as i32, (s.y - target.y) as i32);
        // (skill_blown's count is a `char`)
        skill_blown(c, &s, target, (dist - 1) as i8 as i32, unit::getdir(&s) as i8, BLOWN_NONE);
        let s = unit::get(c, src.id).unwrap_or(s);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        if crate::battle::target::battle_check_target(c, &s, &t, BCT_ENEMY) > 0 {
            weapon_castend_damage_id(c, id, &s, &t, skill_lv, tick, *flag);
        }
    }
}
