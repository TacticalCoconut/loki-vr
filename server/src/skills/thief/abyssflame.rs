//! skills/thief/abyssflame.cpp: class SkillAbyssFlame : public SkillImpl (ABC_ABYSS_FLAME) and class
//! SkillAbyssFlameAttack : public SkillImplRecursiveDamageSplash (ABC_ABYSS_FLAME_ATK).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_MAGIC, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skill::SD_SPLASH;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, status_get_amotion};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::defs::BCT_SELF;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillAbyssFlame;

impl SkillImpl for SkillAbyssFlame {
    fn skill_id(&self) -> u16 {
        ABC_ABYSS_FLAME
    }

    /// SkillAbyssFlame::castendDamageId: flag&1 (a splash target): animation, a damage packet without damage and the
    /// magic hit; else the splash around the caster, then ABC_ABYSS_FLAME_ATK on the target.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
            crate::skill::attack::skill_attack(c, BF_MAGIC, src, src, target, id, skill_lv, tick, *flag);
        } else {
            let f = (*flag | BCT_ENEMY | SD_SPLASH | 1) & !BCT_SELF;
            foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            skill_castend_damage_id(c, &s, &t, ABC_ABYSS_FLAME_ATK, skill_lv, tick, *flag);
        }
    }

    /// SkillAbyssFlame::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 820 * lv;
        *skillratio += 10 * sstatus.spl as i32;
        *skillratio += 30 * lv * pc_checkskill(c, src, ABC_MAGIC_SWORD_M);
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

pub struct SkillAbyssFlameAttack;

impl SkillImpl for SkillAbyssFlameAttack {
    fn skill_id(&self) -> u16 {
        ABC_ABYSS_FLAME_ATK
    }

    crate::splash_skill_defaults!();

    /// SkillAbyssFlameAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 500 * lv;
        *skillratio += 10 * sstatus.spl as i32;
        *skillratio += 15 * lv * pc_checkskill(c, src, ABC_MAGIC_SWORD_M);
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillAbyssFlameAttack {
    /// SkillAbyssFlameAttack::splashSearch: a damage packet without damage and the animation, then the splash.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
