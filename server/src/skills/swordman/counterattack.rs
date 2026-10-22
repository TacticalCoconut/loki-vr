//! skills/swordman/counterattack.cpp: class SkillCounterAttack : public SkillImpl (KN_AUTOCOUNTER). The counter
//! itself is skill_timerskill's KN_AUTOCOUNTER case and the auto-guard / counter checks of battle.cpp.

use crate::battle::calc::Damage;
use crate::battle::{BF_NORMAL, BF_SKILLMASK, BF_WEAPON};
use crate::db::skills::*;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCounterAttack;

impl SkillImpl for SkillCounterAttack {
    fn skill_id(&self) -> u16 {
        KN_AUTOCOUNTER
    }

    /// SkillCounterAttack::modifyDamageData: the counter counts as a normal attack.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        dmg.flag = (dmg.flag & !BF_SKILLMASK) | BF_NORMAL;
    }

    /// SkillCounterAttack::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_addtimerskill(c, &s, tick + 100, target.id, 0, 0, id, skill_lv, BF_WEAPON, *flag);
    }

    /// SkillCounterAttack::modifyHitRate.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 20 / 100) as i16;
    }
}
