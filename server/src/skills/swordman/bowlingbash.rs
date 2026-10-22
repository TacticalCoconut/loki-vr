//! skills/swordman/bowlingbash.cpp: class SkillBowlingBash : public SkillImpl (KN_BOWLINGBASH). RENEWAL: a splash
//! around the target (the pre-renewal gutter-line chain and its bowling_db are not compiled).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::{SD_ANIMATION, SD_SPLASH};
use crate::skills::SkillImpl;
use crate::status::defs::W_2HSWORD;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBowlingBash;

impl SkillImpl for SkillBowlingBash {
    fn skill_id(&self) -> u16 {
        KN_BOWLINGBASH
    }

    /// SkillBowlingBash::modifyDamageData (RENEWAL): a two-handed sword hits 3 times with 2 targets, 4 with 4.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        // sd != nullptr && sd->status.weapon == W_2HSWORD
        if src.kind != BL_PC {
            return;
        }
        let Some(ch) = crate::pc::character_of(c, src.id) else { return };
        if ch.weapon as i32 == W_2HSWORD {
            if dmg.miscflag >= 4 {
                dmg.div_ = 4;
            } else if dmg.miscflag >= 2 {
                dmg.div_ = 3;
            }
        }
    }

    /// SkillBowlingBash::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 40 * skill_lv as i32;
    }

    /// SkillBowlingBash::castendDamageId (RENEWAL).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            let t0 = area::temp(0);
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, if t0 > 0 { SD_ANIMATION | t0 } else { t0 });
        } else {
            let splash = skill_get_splash(id, skill_lv) as i16;
            let n = foreach_in_range(c, target, splash, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, BCT_ENEMY, area::skill_area_sub_count));
            area::set_temp(0, n);
            let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
            foreach_in_range(c, target, splash, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
        }
    }
}
