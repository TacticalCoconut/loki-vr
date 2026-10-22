//! skills/elemental/stonerain.cpp: class SkillStoneRain : public SkillImpl (EL_STONE_RAIN), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::target::battle_get_master;
use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStoneRain;

impl SkillImpl for SkillStoneRain {
    fn skill_id(&self) -> u16 {
        EL_STONE_RAIN
    }

    /// SkillStoneRain::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 200;
    }

    /// SkillStoneRain::castendDamageId: a splash target (flag & 1) takes the skill's own hit; otherwise the skill animation on
    /// the master and the dummy damage packet on the elemental itself, then 30%: every character in the splash range is
    /// hit through skill_castend_damage_id (flag | BCT_ENEMY | 1), 70%: the target alone.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        } else {
            let i = skill_get_splash(id, skill_lv);
            let master = battle_get_master(c, src);
            clif_skill_nodamage(c, Some(src), &master, id, skill_lv as i32, true);
            clif_skill_damage(c, src, src, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
            if ((c.rnd() % 100) as i32) < 30 {
                // map_foreachinrange(skill_area_sub, target, i, BL_CHAR, …)
                let f = *flag | BCT_ENEMY | 1;
                foreach_in_range(c, target, i as i16, BL_CHAR, Wall::Config, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
            } else {
                skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
            }
        }
    }
}
