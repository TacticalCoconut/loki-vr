//! skills/npc/npcignitionbreak.cpp: class SkillNpcIgnitionBreak : public SkillImplRecursiveDamageSplash
//! (NPC_IGNITIONBREAK), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNpcIgnitionBreak;

impl SkillImpl for SkillNpcIgnitionBreak {
    fn skill_id(&self) -> u16 {
        NPC_IGNITIONBREAK
    }

    crate::splash_skill_defaults!();

    /// SkillNpcIgnitionBreak::calculateSkillRatio: by the distance to the caster.
    /// 3x3 cell Damage   = 1000  1500  2000  2500  3000 %
    /// 7x7 cell Damage   = 750   1250  1750  2250  2750 %
    /// 11x11 cell Damage = 500   1000  1500  2000  2500 %
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        // distance_bl(src, target)
        let i = crate::path::distance(src.x as i32 - target.x as i32, src.y as i32 - target.y as i32);
        let lv = skill_lv as i32;
        if i < 2 {
            *base_skillratio += -100 + 500 * (lv + 1);
        } else if i < 4 {
            *base_skillratio += -100 + 250 + 500 * lv;
        } else {
            *base_skillratio += -100 + 500 * lv;
        }
    }

    /// SkillNpcIgnitionBreak::castendNoDamageId (PACKETVER >= 20180207: the skill packet, not the dummy damage one).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        area::set_temp(1, 0);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
        foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
    }
}

impl RecursiveDamageSplash for SkillNpcIgnitionBreak {}
