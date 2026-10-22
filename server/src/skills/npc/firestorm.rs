//! skills/npc/firestorm.cpp: class SkillFireStorm : public SkillImpl (NPC_FIRESTORM), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_MAGIC};
use crate::db::skills::*;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_ANIMATION;
use crate::skills::SkillImpl;
use crate::status::consts::SC_BURNT;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFireStorm;

impl SkillImpl for SkillFireStorm {
    fn skill_id(&self) -> u16 {
        NPC_FIRESTORM
    }

    /// SkillFireStorm::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_BURNT, 100, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillFireStorm::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 200;
    }

    /// SkillFireStorm::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillFireStorm::castendNoDamageId: every enemy in shooting range of the caster (above level 1 without
    /// knockback: flag 4).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut sflag = *flag;
        if skill_lv > 1 {
            sflag |= 4;
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let f = sflag | BCT_ENEMY | SD_ANIMATION | 1;
        foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, area::splash_target(src), Wall::Always, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
    }
}
