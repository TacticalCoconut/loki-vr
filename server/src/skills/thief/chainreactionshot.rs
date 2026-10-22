//! skills/thief/chainreactionshot.cpp: class SkillChainReactionShot : public SkillImplRecursiveDamageSplash
//! (ABC_CHAIN_REACTION_SHOT) and class SkillChainReactionShotAttack : public WeaponSkillImpl
//! (ABC_CHAIN_REACTION_SHOT_ATK).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::novice::{re_lvl_dmod, sce, status_get_amotion};
use crate::skills::{splash_search_default, weapon_castend_damage_id, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_CHASING;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillChainReactionShot;

impl SkillImpl for SkillChainReactionShot {
    fn skill_id(&self) -> u16 {
        ABC_CHAIN_REACTION_SHOT
    }

    crate::splash_skill_defaults!();

    /// SkillChainReactionShot::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 850 * skill_lv as i32;
        *skillratio += 15 * sstatus.con as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillChainReactionShot {
    /// SkillChainReactionShot::splashSearch: the animation, ABC_CHAIN_REACTION_SHOT_ATK on everything around the
    /// target (with the tick 200 ms + amotion later), then the skill's own splash.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        let atk_tick = tick + (200 + status_get_amotion(c, src)) as Tick;
        let f = flag | BCT_ENEMY | SD_SPLASH | 1;
        foreach_in_range(c, target, skill_get_splash(ABC_CHAIN_REACTION_SHOT_ATK, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| {
            skill_area_sub(c, bl, src, ABC_CHAIN_REACTION_SHOT_ATK, skill_lv, atk_tick, f, skill_castend_damage_id)
        });
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}

pub struct SkillChainReactionShotAttack;

impl SkillImpl for SkillChainReactionShotAttack {
    fn skill_id(&self) -> u16 {
        ABC_CHAIN_REACTION_SHOT_ATK
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillChainReactionShotAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        if skill_lv == 4 {
            *skillratio += -100 + 11500;
        } else {
            *skillratio += -100 + 950 + 2650 * lv;
        }
        *skillratio += 15 * sstatus.con as i32;
        if sce(c, src, SC_CHASING).is_some() {
            *skillratio += 1100 * lv;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
