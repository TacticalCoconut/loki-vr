//! skills/npc/npcdragonbreath.cpp: class SkillNpcDragonBreath : public WeaponSkillImpl (NPC_DRAGONBREATH), rAthena
//! e985006. Levels 1-5 are like RK_DRAGONBREATH (fire), 6-10 like RK_DRAGONBREATH_WATER.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_SKILL};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_BURNING, SC_FREEZING, SC_HIDING};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNpcDragonBreath;

impl SkillImpl for SkillNpcDragonBreath {
    fn skill_id(&self) -> u16 {
        NPC_DRAGONBREATH
    }

    /// SkillNpcDragonBreath::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        if skill_lv > 5 {
            sc::sc_start4(c, Some(src), target, SC_FREEZING, 50, skill_lv as i32, 1000, src.id as i32, 0, skill_get_time(id, skill_lv) as i64);
        } else {
            sc::sc_start4(c, Some(src), target, SC_BURNING, 50, skill_lv as i32, 1000, src.id as i32, 0, skill_get_time(id, skill_lv) as i64);
        }
    }

    /// SkillNpcDragonBreath::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        if skill_lv > 5 {
            *base_skillratio += 500 + 500 * (skill_lv as i32 - 5); // Level 6-10 is using water element, like RK_DRAGONBREATH_WATER
        } else {
            *base_skillratio += 500 + 500 * skill_lv as i32; // Level 1-5 is using fire element, like RK_DRAGONBREATH
        }
    }

    /// SkillNpcDragonBreath::castendDamageId: a hidden target only shows the skill on the caster; else
    /// WeaponSkillImpl::castendDamageId.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if sce(c, target, SC_HIDING).is_some() {
            clif_skill_nodamage(c, Some(src), src, id, skill_lv as i32, true);
        } else {
            weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
        }
    }

    /// SkillNpcDragonBreath::castendPos2: every unit of the area around the cell (the cell is kept as the cast
    /// center).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // Cast center might be relevant later (e.g. for knockback direction)
        area::set_temp(4, x as i32);
        area::set_temp(5, y as i32);
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
        });
    }
}
