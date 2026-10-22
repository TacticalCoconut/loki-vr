//! skills/npc/jackfrost2.cpp: class SkillJackFrost2 : public SkillImplRecursiveDamageSplash (NPC_JACKFROST), rAthena
//! e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_FREEZE, SC_FREEZING};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillJackFrost2;

impl SkillImpl for SkillJackFrost2 {
    fn skill_id(&self) -> u16 {
        NPC_JACKFROST
    }

    crate::splash_skill_defaults!();

    /// SkillJackFrost2::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_FREEZE, 200, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillJackFrost2::calculateSkillRatio: more against a target in SC_FREEZING (RE_LVL_DMOD 100), else RE_LVL_DMOD
    /// 150.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if sce(c, target, SC_FREEZING).is_some() {
            *skillratio += 900 + 300 * skill_lv as i32;
            re_lvl_dmod(c, src, skillratio, 100);
        } else {
            *skillratio += 400 + 100 * skill_lv as i32;
            re_lvl_dmod(c, src, skillratio, 150);
        }
    }

    /// SkillJackFrost2::castendNoDamageId: the splash around the target.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
    }
}

impl RecursiveDamageSplash for SkillJackFrost2 {}
