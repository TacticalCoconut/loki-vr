//! skills/mercenary/mercenary_magnumbreak.cpp: class SkillMercenaryMagnumBreak : public SkillImpl (MS_MAGNUM).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_ANIMATION;
use crate::skills::SkillImpl;
use crate::status::consts::SC_SUB_WEAPONPROPERTY;
use crate::status::defs::ELE_FIRE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryMagnumBreak;

impl SkillImpl for SkillMercenaryMagnumBreak {
    fn skill_id(&self) -> u16 {
        MS_MAGNUM
    }

    /// SkillMercenaryMagnumBreak::calculateSkillRatio: inner 3x3 circle takes 100%+20%*level damage, outer 5x5
    /// circle 100%+10%*level [Playtester].
    fn calculate_skill_ratio(&self, _c: &Ctx, wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        if wd.miscflag == 1 {
            *base_skillratio += 20 * skill_lv as i32;
        } else {
            *base_skillratio += 10 * skill_lv as i32;
        }
    }

    /// SkillMercenaryMagnumBreak::castendDamageId: for players, damage depends on distance, so add it to flag if it
    /// is > 1; cannot hit hidden targets.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            // sd ? distance_bl(src, target) : 0
            let dist = if src.kind == BL_PC { crate::path::distance((src.x - target.x) as i32, (src.y - target.y) as i32) } else { 0 };
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag | SD_ANIMATION | dist);
        }
    }

    /// SkillMercenaryMagnumBreak::castendNoDamageId: the splash around the caster (map_foreachinshootrange), then
    /// 20% of the damage becomes fire element (RENEWAL: SC_SUB_WEAPONPROPERTY).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        area::set_temp(1, 0);
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_SKILL | BL_CHAR, Wall::Always, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
        // Initiate 20% of your damage becomes fire element.
        let s = unit::get(c, src.id).unwrap_or(s);
        sc::sc_start4(c, Some(&s), &s, SC_SUB_WEAPONPROPERTY, 100, ELE_FIRE, 20, id as i32, 0, skill_get_time2(id, skill_lv) as i64);
    }

    /// SkillMercenaryMagnumBreak::modifyHitRate.
    fn modify_hit_rate(&self, _c: &Ctx, hit_rate: &mut i16, _src: &Unit, _target: &Unit, skill_lv: u16) {
        let h = *hit_rate as i32;
        *hit_rate = (h + h * 10 * skill_lv as i32 / 100) as i16;
    }
}
