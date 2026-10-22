//! skills/mage/frostnova.cpp: class SkillFrostNova : public SkillImpl (WZ_FROSTNOVA).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_MAGIC};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::area::{self, foreach_in_range, skill_attack_area, splash_target, Wall};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::SC_FREEZE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFrostNova;

impl SkillImpl for SkillFrostNova {
    fn skill_id(&self) -> u16 {
        WZ_FROSTNOVA
    }

    /// SkillFrostNova::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        area::set_temp(1, 0);
        // map_foreachinshootrange(skill_attack_area, src, skill_get_splash(id, lv), splash_target(src), BF_MAGIC, src,
        // src, id, lv, tick, flag, BCT_ENEMY)
        let f = *flag;
        foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, splash_target(src), Wall::Always, |c, bl| {
            skill_attack_area(c, bl, BF_MAGIC, src, src, id, skill_lv, tick, f, BCT_ENEMY)
        });
    }

    /// SkillFrostNova::calculateSkillRatio: in renewal the damage formula is identical to MG_FROSTDIVER (the
    /// pre-renewal formula is dropped).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 10 * skill_lv as i32;
    }

    /// SkillFrostNova::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let lv = skill_lv as i32;
        // (sd != nullptr) ? skill_lv*5+33 : skill_lv*3+35
        let rate = if src.kind == BL_PC { lv * 5 + 33 } else { lv * 3 + 35 };
        sc::sc_start(c, Some(src), target, SC_FREEZE, rate, lv, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}
