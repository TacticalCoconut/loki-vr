//! skills/taekwon/stormkick.cpp: class SkillStormKick : public SkillImpl (TK_STORMKICK).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_WEAPON};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::npc::acidbreath::skill_attack_area;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStormKick;

impl SkillImpl for SkillStormKick {
    fn skill_id(&self) -> u16 {
        TK_STORMKICK
    }

    /// SkillStormKick::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 60 + 20 * skill_lv as i32;
    }

    /// SkillStormKick::castendDamageId: map_foreachinshootrange(skill_attack_area, src, splash, BL_CHAR | BL_SKILL,
    /// BF_WEAPON, src, src, id, lv, tick, flag, BCT_ENEMY) after skill_area_temp[1] = 0 (no unit is the first target).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        area::set_temp(1, 0);
        let f = *flag;
        foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Always, |c, bl| {
            skill_attack_area(c, bl, BF_WEAPON, src, src, id, skill_lv, tick, f, BCT_ENEMY)
        });
    }
}
