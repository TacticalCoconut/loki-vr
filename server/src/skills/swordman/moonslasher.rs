//! skills/swordman/moonslasher.cpp: class SkillMoonSlasher : public SkillImplRecursiveDamageSplash (LG_MOONSLASHER).

use crate::battle::calc::Damage;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, status_get_amotion};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_OVERBRANDREADY;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMoonSlasher;

impl SkillImpl for SkillMoonSlasher {
    fn skill_id(&self) -> u16 {
        LG_MOONSLASHER
    }

    crate::splash_skill_defaults!();

    /// SkillMoonSlasher::castendNoDamageId: the splash around the caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_castend_damage_id(c, src, src, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillMoonSlasher::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        // ((sd) ? pc_checkskill(sd, LG_OVERBRAND) * 80 : 0)
        *skillratio += -100 + 120 * skill_lv as i32 + pc_checkskill(c, src, LG_OVERBRAND) * 80;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillMoonSlasher::applyAdditionalEffects: the caster gets SC_OVERBRANDREADY.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), src, SC_OVERBRANDREADY, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }
}

impl RecursiveDamageSplash for SkillMoonSlasher {
    /// SkillMoonSlasher::splashSearch: the dummy damage packet on the target first.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, self.skill_id(), skill_lv as i32, DMG_SINGLE);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        splash_search_default(self, c, src, &t, skill_lv, tick, flag);
    }
}
