//! skills/gunslinger/hammerofgod.cpp: class SkillHammerOfGod : public SkillImplRecursiveDamageSplash
//! (RL_HAMMER_OF_GOD), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_poseffect;
use crate::skill::state::{self as pcskill, MAX_SKILL_CRIMSON_MARKER};
use crate::skill::{SD_ANIMATION, SD_SPLASH};
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_castend_pos2, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_C_MARKER;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHammerOfGod;

impl SkillImpl for SkillHammerOfGod {
    fn skill_id(&self) -> u16 {
        RL_HAMMER_OF_GOD
    }

    /// SkillHammerOfGod::castendDamageId: a splash hit (flag & 1) carries SD_ANIMATION; the cast marks flag 8 when the
    /// target is one of the player's Crimson Marker targets, shows the hammer at the target's cell and hits the
    /// enemies around it (flag | 1).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag | SD_ANIMATION);
            return;
        }
        if src.kind == BL_PC && sce(c, target, SC_C_MARKER).is_some() {
            let marks = pcskill::get(c, src.id).c_marker;
            if (0..MAX_SKILL_CRIMSON_MARKER).any(|i| marks.get(i).copied() == Some(target.id)) {
                *flag |= 8;
            }
        }
        clif_skill_poseffect(c, src, id, 1, target.x, target.y, c.tick());
        let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
        foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
    }

    /// SkillImplRecursiveDamageSplash::castendPos2 (not overridden).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
    }

    /// SkillHammerOfGod::calculateSkillRatio: 100% per level, + 400% (a marked target: flag 8) or 150% per coin used
    /// (sd->spiritball_old), RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 100 * skill_lv as i32;
        if src.kind == BL_PC {
            let coins = pcskill::get(c, src.id).spiritball_old as i32;
            if wd.miscflag & 8 != 0 {
                *skillratio += 400 * coins;
            } else {
                *skillratio += 150 * coins;
            }
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillHammerOfGod {}
